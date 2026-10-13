using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ForkPlus.Plugins.Frontend
{
	/// <summary>
	/// 把 Vue 单文件组件（SFC）源码组装成**自包含 HTML**（供「渲染」模式并排双 WebView 渲染）。
	///
	/// 运行时来自随包内嵌的 Vue 3 全量构建（<c>Assets/vue.global.prod.js</c>，含模板编译器），
	/// 内联进页面后对 <c>template</c> 做运行期编译，无需网络、无需自定义协议，
	/// WebView <c>NavigateToString</c> 即可显示。
	///
	/// 这不是完整的 <c>@vue/compiler-sfc</c>，而是**尽力而为**的轻量转换：
	/// <list type="bullet">
	/// <item>提取顶层 &lt;template&gt; / &lt;script&gt; / &lt;script setup&gt; / &lt;style&gt; 块；</item>
	/// <item><c>&lt;script&gt;</c>（Options API）：<c>export default {...}</c> 改写为 <c>return {...}</c>，
	/// 剥离 import，<c>import { ... } from 'vue'</c> 变成从全局 <c>Vue</c> 解构；</item>
	/// <item><c>&lt;script setup&gt;</c>（组合式 API）：整体包成 <c>setup()</c>，末尾按顶层声明
	/// 自动 <c>return</c> 出所有命名绑定，使模板可访问；</item>
	/// <item>其它模块 import（本地文件 / npm 包）无法解析，按未定义占位——组件若强依赖它们会报错，
	/// 错误会渲染在页面里而不是静默失败。</item>
	/// </list>
	/// 任何解析 / 运行期异常都在页面内以红色文本呈现，保证「渲染」模式始终有可见结果。
	/// </summary>
	internal static class VueComponentCompiler
	{
		private const string RuntimeResourceName = "vue.global.prod.js";

		private static readonly object Sync = new object();

		private static string _runtime;

		private static readonly Regex ScriptSetupRegex = new Regex(
			"<script\\b[^>]*\\bsetup\\b[^>]*>(?<body>[\\s\\S]*?)</script\\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly Regex ScriptRegex = new Regex(
			"<script\\b(?![^>]*\\bsetup\\b)[^>]*>(?<body>[\\s\\S]*?)</script\\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly Regex StyleRegex = new Regex(
			"<style\\b[^>]*>(?<body>[\\s\\S]*?)</style\\s*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		// import { a, b as c } from 'vue'
		private static readonly Regex VueImportRegex = new Regex(
			"import\\s*\\{(?<names>[^}]*)\\}\\s*from\\s*['\"]vue['\"]\\s*;?",
			RegexOptions.Compiled);

		// 其它 import（含 `import './x.css'` 形式）
		private static readonly Regex AnyImportRegex = new Regex(
			"import\\s+(?:[^;'\\\"]*?from\\s*)?['\"][^'\\\"]*['\"]\\s*;?",
			RegexOptions.Compiled);

		private static readonly Regex ExportDefaultRegex = new Regex(
			"\\bexport\\s+default\\b",
			RegexOptions.Compiled);

		private static readonly Regex ConstDeclRegex = new Regex(
			"(?:^|[\\n;{])\\s*(?:export\\s+)?(?:const|let|var)\\s+(?<name>[A-Za-z_$][\\w$]*)",
			RegexOptions.Compiled);

		private static readonly Regex ObjectDestructureRegex = new Regex(
			"(?:^|[\\n;{])\\s*(?:export\\s+)?(?:const|let|var)\\s*\\{(?<names>[^}]*)\\}",
			RegexOptions.Compiled);

		private static readonly Regex ArrayDestructureRegex = new Regex(
			"(?:^|[\\n;{])\\s*(?:export\\s+)?(?:const|let|var)\\s*\\[(?<names>[^\\]]*)\\]",
			RegexOptions.Compiled);

		private static readonly Regex FunctionDeclRegex = new Regex(
			"(?:^|[\\n;{])\\s*(?:export\\s+)?(?:async\\s+)?function\\s+(?<name>[A-Za-z_$][\\w$]*)",
			RegexOptions.Compiled);

		/// <summary>把 SFC 源码组装成自包含 HTML；源码为空返回 null。</summary>
		internal static string Compose(string sfcSource)
		{
			if (string.IsNullOrEmpty(sfcSource))
			{
				return null;
			}
			string runtime = LoadRuntime();
			if (runtime == null)
			{
				return ErrorPage("Vue runtime is not available.");
			}

			string template = ExtractTemplate(sfcSource);
			string scriptSetup = FirstBody(ScriptSetupRegex, sfcSource);
			string script = FirstBody(ScriptRegex, sfcSource);
			string styles = ExtractStyles(sfcSource);

			StringBuilder sb = new StringBuilder(sfcSource.Length + runtime.Length + 1024);
			sb.Append("<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">\n");
			sb.Append("<style>\n")
				.Append("html,body{margin:0;padding:10px;font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;}")
				.Append("#app{min-height:8px}\n")
				.Append("</style>\n");
			if (!string.IsNullOrEmpty(styles))
			{
				sb.Append("<style>\n").Append(styles).Append("\n</style>\n");
			}
			sb.Append("<script>\n").Append(runtime).Append("\n</script>\n");
			sb.Append("</head><body>\n<div id=\"app\"></div>\n<script>\n");
			sb.Append("(function(){\n");
			sb.Append("var __tpl = ").Append(JsStringLiteral(template ?? string.Empty)).Append(";\n");
			sb.Append("try{\n");
			sb.Append("var __opts = ");
			if (!string.IsNullOrEmpty(scriptSetup))
			{
				sb.Append("({ setup: ").Append(BuildSetup(scriptSetup)).Append(" })");
			}
			else if (!string.IsNullOrEmpty(script))
			{
				sb.Append(BuildOptions(script));
			}
			else
			{
				sb.Append("{}");
			}
			sb.Append(";\n");
			sb.Append("if(__opts.template == null){__opts.template=__tpl;}\n");
			sb.Append("Vue.createApp(__opts).mount('#app');\n");
			sb.Append("}catch(e){\n");
			sb.Append("var m=(e&&e.message)?e.message:String(e);\n");
			sb.Append("document.getElementById('app').innerHTML=")
				.Append("'<pre style=\"color:#c0392b;white-space:pre-wrap;font-family:monospace\">'+")
				.Append("m.replace(/&/g,'&amp;').replace(/</g,'&lt;')+'</pre>';\n");
			sb.Append("}\n})();\n");
			sb.Append("</script>\n</body></html>\n");
			return sb.ToString();
		}

		// ---- 脚本转换 ----

		/// <summary>Options API：<c>export default {...}</c> → <c>return {...}</c>，剥 import。</summary>
		private static string BuildOptions(string script)
		{
			string body = StripImports(script, out string prelude);
			body = ExportDefaultRegex.Replace(body, "return", 1);
			return "(function(){\n" + prelude + body + "\n})()";
		}

		/// <summary>组合式 API（&lt;script setup&gt;）：包成 setup()，末尾返回所有顶层命名绑定。</summary>
		private static string BuildSetup(string script)
		{
			string body = StripImports(script, out string prelude);
			List<string> names = CollectDeclaredNames(body);
			StringBuilder sb = new StringBuilder();
			sb.Append("function(){\n").Append(prelude).Append(body).Append("\nreturn {")
				.Append(string.Join(", ", names)).Append("};\n}");
			return sb.ToString();
		}

		/// <summary>剥离 import：<c>from 'vue'</c> 的具名导入变成从全局 <c>Vue</c> 解构（写入 prelude），
		/// 其余 import 一律删除（无法解析，保持模板可渲染）。</summary>
		private static string StripImports(string script, out string prelude)
		{
			List<string> bindings = new List<string>();
			string body = VueImportRegex.Replace(script, delegate (Match match)
			{
				foreach (string raw in match.Groups["names"].Value.Split(','))
				{
					string name = raw.Trim();
					if (name.Length == 0)
					{
						continue;
					}
					int asIndex = name.IndexOf(" as ", StringComparison.Ordinal);
					if (asIndex > 0)
					{
						// `ref as r` → `ref: r`
						name = name.Substring(0, asIndex).Trim() + ": " + name.Substring(asIndex + 4).Trim();
					}
					bindings.Add(name);
				}
				return string.Empty;
			});
			body = AnyImportRegex.Replace(body, string.Empty);
			prelude = bindings.Count == 0
				? string.Empty
				: "var {" + string.Join(", ", bindings) + "} = Vue;\n";
			return body;
		}

		/// <summary>收集顶层命名绑定（const/let/var 具名与解构、function 声明），用于 setup 自动 return。</summary>
		private static List<string> CollectDeclaredNames(string body)
		{
			List<string> names = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			Action<string> add = delegate (string name)
			{
				if (!string.IsNullOrEmpty(name) && seen.Add(name))
				{
					names.Add(name);
				}
			};
			foreach (Match match in ConstDeclRegex.Matches(body))
			{
				add(match.Groups["name"].Value);
			}
			foreach (Match match in FunctionDeclRegex.Matches(body))
			{
				add(match.Groups["name"].Value);
			}
			foreach (Match match in ObjectDestructureRegex.Matches(body))
			{
				foreach (string raw in match.Groups["names"].Value.Split(','))
				{
					string name = raw.Trim();
					int colon = name.IndexOf(':');
					if (colon > 0)
					{
						name = name.Substring(0, colon).Trim();
					}
					if (IsIdentifier(name))
					{
						add(name);
					}
				}
			}
			foreach (Match match in ArrayDestructureRegex.Matches(body))
			{
				foreach (string raw in match.Groups["names"].Value.Split(','))
				{
					string name = raw.Trim();
					if (IsIdentifier(name))
					{
						add(name);
					}
				}
			}
			return names;
		}

		private static bool IsIdentifier(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			if (!(char.IsLetter(name[0]) || name[0] == '_' || name[0] == '$'))
			{
				return false;
			}
			foreach (char c in name)
			{
				if (!(char.IsLetterOrDigit(c) || c == '_' || c == '$'))
				{
					return false;
				}
			}
			return true;
		}

		// ---- 块提取 ----

		/// <summary>取第一个匹配块的正文（无匹配返回 null）。</summary>
		private static string FirstBody(Regex regex, string source)
		{
			Match match = regex.Match(source);
			return match.Success ? match.Groups["body"].Value : null;
		}

		private static string ExtractStyles(string source)
		{
			StringBuilder sb = new StringBuilder();
			foreach (Match match in StyleRegex.Matches(source))
			{
				// scoped / module 等属性无法在此还原作用域，按普通样式保留（尽力而为）。
				sb.Append(match.Groups["body"].Value).Append('\n');
			}
			return sb.ToString();
		}

		/// <summary>提取顶层 &lt;template&gt; 正文；按同名标签深度配对，兼容内部嵌套 &lt;template&gt;。</summary>
		private static string ExtractTemplate(string source)
		{
			int start = IndexOfOpenTag(source, "template", 0);
			if (start < 0)
			{
				return null;
			}
			int gt = source.IndexOf('>', start);
			if (gt < 0)
			{
				return null;
			}
			int depth = 1;
			int pos = gt + 1;
			int innerStart = pos;
			while (pos < source.Length)
			{
				int open = IndexOfOpenTag(source, "template", pos);
				int close = IndexOfCloseTag(source, "template", pos);
				if (close < 0)
				{
					return source.Substring(innerStart);
				}
				if (open >= 0 && open < close)
				{
					depth++;
					int openGt = source.IndexOf('>', open);
					pos = openGt >= 0 ? openGt + 1 : open + 9;
					continue;
				}
				depth--;
				if (depth == 0)
				{
					return source.Substring(innerStart, close - innerStart);
				}
				pos = close + "</template".Length;
			}
			return null;
		}

		private static int IndexOfOpenTag(string source, string name, int from)
		{
			string needle = "<" + name;
			int index = from;
			while (index < source.Length)
			{
				int found = source.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
				if (found < 0)
				{
					return -1;
				}
				int after = found + needle.Length;
				if (after >= source.Length || char.IsWhiteSpace(source[after]) || source[after] == '>' || source[after] == '/')
				{
					return found;
				}
				index = found + 1;
			}
			return -1;
		}

		private static int IndexOfCloseTag(string source, string name, int from)
		{
			string needle = "</" + name;
			int index = from;
			while (index < source.Length)
			{
				int found = source.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
				if (found < 0)
				{
					return -1;
				}
				int after = found + needle.Length;
				if (after >= source.Length || char.IsWhiteSpace(source[after]) || source[after] == '>')
				{
					return found;
				}
				index = found + 1;
			}
			return -1;
		}

		// ---- 运行时 ----

		/// <summary>懒加载内嵌的 Vue 运行时（进程级缓存）；缺失返回 null。</summary>
		private static string LoadRuntime()
		{
			lock (Sync)
			{
				if (_runtime != null)
				{
					return _runtime.Length == 0 ? null : _runtime;
				}
				try
				{
					using (Stream stream = typeof(VueComponentCompiler).Assembly.GetManifestResourceStream(RuntimeResourceName))
					{
						if (stream == null)
						{
							_runtime = string.Empty;
							return null;
						}
						using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
						{
							_runtime = reader.ReadToEnd();
						}
					}
				}
				catch (Exception)
				{
					_runtime = string.Empty;
				}
				return _runtime.Length == 0 ? null : _runtime;
			}
		}

		// ---- 文本工具 ----

		/// <summary>把文本编码成 JS 字符串字面量；顺带把 <c>&lt;/</c> 转义为 <c>&lt;\/</c>，避免提前闭合 &lt;script&gt;。</summary>
		private static string JsStringLiteral(string text)
		{
			StringBuilder sb = new StringBuilder(text.Length + 16);
			sb.Append('"');
			foreach (char c in text)
			{
				switch (c)
				{
					case '"':
						sb.Append("\\\"");
						break;
					case '\\':
						sb.Append("\\\\");
						break;
					case '\n':
						sb.Append("\\n");
						break;
					case '\r':
						sb.Append("\\r");
						break;
					case '\t':
						sb.Append("\\t");
						break;
					case '\u2028':
						sb.Append("\\u2028");
						break;
					case '\u2029':
						sb.Append("\\u2029");
						break;
					default:
						if (c < ' ')
						{
							sb.Append("\\u").Append(((int)c).ToString("x4"));
						}
						else
						{
							sb.Append(c);
						}
						break;
				}
			}
			sb.Append('"');
			return sb.ToString().Replace("</", "<\\/");
		}

		private static string ErrorPage(string message)
		{
			return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>"
				+ "<pre style=\"color:#c0392b;white-space:pre-wrap;font-family:monospace\">"
				+ System.Net.WebUtility.HtmlEncode(message) + "</pre></body></html>";
		}
	}
}