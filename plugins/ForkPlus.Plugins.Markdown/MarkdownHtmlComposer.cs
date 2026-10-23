using System.Text;
using Markdig;

namespace ForkPlus.Plugins.Markdown
{
	/// <summary>
	/// 把一侧 Markdown 组装成**自包含 HTML**（供「渲染」模式并排双 WebView 预览）。
	///
	/// Markdig（高级扩展管线：表格 / 任务列表 / 自动链接 / 删除线…）转 HTML，套一层跟随系统
	/// 明暗主题的排版样式壳（GitHub 风格：衬线正文、等宽代码块、表格边框、引用条）。
	/// 内容与样式全部内联生成，无外部资源引用，<c>NavigateToString</c> 直接可渲染。
	///
	/// 受预算约束：Windows WebView2 的 <c>NavigateToString</c> 有 2MB 硬限制，超限导航静默
	/// 失败（预览一片空白，见 CHM / EPUB / Patch 组装器同款注释）。逼近 90 万字符预算即截断，
	/// 截断点回退到最近一个块级闭合标签，并附说明。
	/// </summary>
	internal static class MarkdownHtmlComposer
	{
		/// <summary>自包含文档的字符预算（与 PatchHtmlComposer 同口径，稳低于 WebView2 的 2MB 硬限制）。</summary>
		private const long MaxChars = 900_000;

		/// <summary>Markdig 管线（进程级复用；渲染与大纲解析共用，保证两模式口径一致）。</summary>
		internal static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.Build();

		/// <summary>
		/// 组装一侧 Markdown 的自包含 HTML；markdown 为 null / 空白返回 null（该侧占位）。
		/// 超预算时截断到最近的块级闭合标签并附说明。
		/// </summary>
		internal static string Compose(string markdown)
		{
			if (string.IsNullOrEmpty(markdown))
			{
				return null;
			}
			string body = Markdig.Markdown.ToHtml(markdown, Pipeline);
			if (string.IsNullOrEmpty(body))
			{
				return null;
			}
			StringBuilder html = new StringBuilder(4096 + (body.Length < MaxChars ? body.Length : (int)MaxChars));
			html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
			html.Append(":root{--bg:#ffffff;--fg:#1f2328;--muted:#59636e;--line:#d1d9e0;--card:#f6f8fa;"
				+ "--code-bg:#f6f8fa;--code-fg:#1f2328;--quote:#59636e;--link:#0969da}");
			html.Append("@media (prefers-color-scheme:dark){:root{--bg:#1c2128;--fg:#e6edf3;--muted:#9198a1;--line:#3d444d;"
				+ "--card:#262c36;--code-bg:#262c36;--code-fg:#e6edf3;--quote:#9198a1;--link:#8ddbff}}");
			html.Append("body{margin:0;padding:14px 18px;background:var(--bg);color:var(--fg);"
				+ "font:14px/1.6 -apple-system,'Segoe UI','PingFang SC','Microsoft YaHei',sans-serif;"
				+ "overflow-wrap:anywhere;}");
			html.Append("h1,h2,h3,h4,h5,h6{margin:1.2em 0 0.5em;line-height:1.25;}");
			html.Append("h1{font-size:1.7em;border-bottom:1px solid var(--line);padding-bottom:0.3em;}");
			html.Append("h2{font-size:1.4em;border-bottom:1px solid var(--line);padding-bottom:0.25em;}");
			html.Append("h3{font-size:1.2em;}h4{font-size:1.05em;}h5,h6{font-size:1em;}");
			html.Append("p{margin:0.6em 0;}");
			html.Append("a{color:var(--link);}");
			html.Append("code{font-family:Consolas,Menlo,monospace;font-size:0.9em;background:var(--code-bg);"
				+ "color:var(--code-fg);border-radius:4px;padding:0.15em 0.35em;}");
			html.Append("pre{background:var(--code-bg);border:1px solid var(--line);border-radius:6px;"
				+ "padding:10px 12px;overflow:auto;}");
			html.Append("pre code{background:none;border:none;padding:0;font-size:0.9em;}");
			html.Append("blockquote{margin:0.8em 0;padding:0.1em 12px;color:var(--quote);"
				+ "border-left:4px solid var(--line);}");
			html.Append("table{border-collapse:collapse;margin:0.8em 0;display:block;max-width:100%;overflow:auto;}");
			html.Append("th,td{border:1px solid var(--line);padding:5px 10px;text-align:left;}");
			html.Append("th{background:var(--card);font-weight:600;}");
			html.Append("img{max-width:100%;}");
			html.Append("hr{border:none;border-top:1px solid var(--line);margin:1.4em 0;}");
			html.Append("ul,ol{padding-left:1.6em;}");
			html.Append("li{margin:0.25em 0;}");
			html.Append(".note{color:var(--muted);font-size:12px;margin:8px 2px;}");
			html.Append("</style></head><body>");
			if (html.Length + body.Length > MaxChars)
			{
				int budget = (int)(MaxChars - html.Length);
				if (budget < body.Length)
				{
					// 截断点回退到预算内最近一个闭合标签，避免拦腰截断未闭合元素破坏整页渲染。
					int cut = body.LastIndexOf("</", budget - 1, System.StringComparison.Ordinal);
					if (cut > 0)
					{
						int close = body.IndexOf('>', cut);
						body = body.Substring(0, close > 0 ? close + 1 : cut);
					}
					else
					{
						body = body.Substring(0, budget);
					}
				}
				html.Append(body);
				html.Append("<div class=\"note\">")
					.Append(Escape(MarkdownStrings.T("Truncated: document exceeds the preview budget.")))
					.Append("</div>");
			}
			else
			{
				html.Append(body);
			}
			html.Append("</body></html>");
			return html.ToString();
		}

		/// <summary>HTML 转义（说明文案进文档前统一转义）。</summary>
		private static string Escape(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
				.Replace("\"", "&quot;");
		}
	}
}
