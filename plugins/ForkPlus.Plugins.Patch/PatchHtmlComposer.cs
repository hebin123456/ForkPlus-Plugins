using System;
using System.Collections.Generic;
using System.Text;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>
	/// 把一侧补丁组装成**自包含 HTML**（供「预览」模式并排双 WebView 渲染）。
	///
	/// 按文件分卡片：文件头（路径 + 改动类型 + 增删统计），其下 hunk 表——旧行号 / 新行号 /
	/// 着色行（新增绿 / 删除红 / 上下文原样 / hunk 头蓝），跟随系统明暗主题。
	/// 内容与样式全部内联生成，无外部资源引用，<c>NavigateToString</c> 直接可渲染。
	///
	/// 受预算约束：Windows WebView2 的 <c>NavigateToString</c> 有 2MB 硬限制，超限导航静默
	/// 失败（预览一片空白，见 CHM / EPUB 组装器同款注释）。逼近 90 万字符预算即截断，并附说明。
	/// </summary>
	internal static class PatchHtmlComposer
	{
		/// <summary>自包含文档的字符预算（内联产物以 ASCII / UTF-8 文本为主，两条口径都稳低于 2MB）。</summary>
		private const long MaxChars = 900_000;

		/// <summary>组装一侧补丁的自包含 HTML；无内容返回 null。</summary>
		internal static string Compose(PatchDocument document)
		{
			if (document == null || document.Files.Count == 0)
			{
				return null;
			}
			StringBuilder html = new StringBuilder(4096);
			html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
			html.Append(":root{--bg:#ffffff;--fg:#1f2328;--muted:#59636e;--line:#d1d9e0;--card:#f6f8fa;"
				+ "--add-bg:#e6ffec;--add-fg:#1a7f37;--del-bg:#ffebe9;--del-fg:#cf222e;--hunk-bg:#ddf4ff;--hunk-fg:#0969da;--ln:#8c959f}");
			html.Append("@media (prefers-color-scheme:dark){:root{--bg:#1c2128;--fg:#e6edf3;--muted:#9198a1;--line:#3d444d;"
				+ "--card:#262c36;--add-bg:#123826;--add-fg:#7ee2b8;--del-bg:#4a2229;--del-fg:#ffb1a9;--hunk-bg:#1d3b5c;--hunk-fg:#8ddbff;--ln:#6e7681}}");
			html.Append("body{margin:0;padding:10px;background:var(--bg);color:var(--fg);"
				+ "font:13px/1.45 -apple-system,'Segoe UI','PingFang SC','Microsoft YaHei',sans-serif;}");
			html.Append(".file{border:1px solid var(--line);border-radius:6px;margin:0 0 10px;overflow:hidden;}");
			html.Append(".fh{display:flex;justify-content:space-between;gap:8px;background:var(--card);padding:6px 10px;"
				+ "font-family:Consolas,Menlo,monospace;font-size:12px;}");
			html.Append(".fh .p{overflow-wrap:anywhere;font-weight:600;}");
			html.Append(".fh .st{white-space:nowrap;color:var(--muted);}");
			html.Append("table{border-collapse:collapse;width:100%;table-layout:fixed;}");
			html.Append("td{padding:0 8px;vertical-align:top;overflow-wrap:anywhere;}");
			html.Append("td.ln{width:44px;text-align:right;color:var(--ln);font-family:Consolas,Menlo,monospace;user-select:none;}");
			html.Append("td.code{font-family:Consolas,Menlo,monospace;font-size:12px;white-space:pre-wrap;}");
			html.Append("tr.add td.code{background:var(--add-bg);color:var(--add-fg);}");
			html.Append("tr.del td.code{background:var(--del-bg);color:var(--del-fg);}");
			html.Append("tr.hh td{background:var(--hunk-bg);color:var(--hunk-fg);font-family:Consolas,Menlo,monospace;"
				+ "font-size:12px;padding:2px 8px;}");
			html.Append(".note{color:var(--muted);font-size:12px;margin:8px 2px;}");
			html.Append("</style></head><body>");
			long used = html.Length;
			int shownFiles = 0;
			for (int index = 0; index < document.Files.Count; index++)
			{
				PatchFile file = document.Files[index];
				string fileHtml = RenderFile(file);
				if (used + fileHtml.Length > MaxChars)
				{
					html.Append("<div class=\"note\">").Append(Escape(PatchStrings.F("Truncated: showing first {0} files", shownFiles)))
						.Append("</div>");
					break;
				}
				html.Append(fileHtml);
				used += fileHtml.Length;
				shownFiles++;
			}
			html.Append("</body></html>");
			return html.ToString();
		}

		/// <summary>渲染单个文件的卡片（文件头 + 全部 hunk 表）。</summary>
		private static string RenderFile(PatchFile file)
		{
			StringBuilder sb = new StringBuilder(1024);
			string path = !string.IsNullOrEmpty(file.NewPath) ? file.NewPath : (file.OldPath ?? string.Empty);
			sb.Append("<div class=\"file\"><div class=\"fh\"><span class=\"p\">")
				.Append(Escape(path))
				.Append("</span><span class=\"st\">");
			if (file.IsBinary)
			{
				sb.Append(Escape(PatchStrings.T("binary file")));
			}
			else
			{
				sb.Append("+").Append(file.Additions).Append(" −").Append(file.Deletions)
					.Append(" · ").Append(Escape(PatchStrings.F("{0} hunks", file.Hunks.Count)))
					.Append(" · ").Append(Escape(ChangeLabel(file.Change)));
			}
			sb.Append("</span></div>");
			if (file.IsBinary)
			{
				sb.Append("</div>");
				return sb.ToString();
			}
			sb.Append("<table>");
			foreach (PatchHunk hunk in file.Hunks)
			{
				sb.Append("<tr class=\"hh\"><td class=\"ln\"></td><td class=\"ln\"></td><td class=\"code\">@@ -")
					.Append(hunk.OldStart).Append(',').Append(hunk.OldCount)
					.Append(" +").Append(hunk.NewStart).Append(',').Append(hunk.NewCount)
					.Append(" @@");
				if (!string.IsNullOrEmpty(hunk.Section))
				{
					sb.Append(' ').Append(Escape(hunk.Section));
				}
				sb.Append("</td></tr>");
				int oldLine = hunk.OldStart;
				int newLine = hunk.NewStart;
				foreach (PatchLine line in hunk.Lines)
				{
					switch (line.Kind)
					{
					case PatchLineKind.Add:
						sb.Append("<tr class=\"add\"><td class=\"ln\"></td><td class=\"ln\">").Append(newLine++)
							.Append("</td><td class=\"code\">").Append(Escape(line.Text)).Append("</td></tr>");
						break;
					case PatchLineKind.Delete:
						sb.Append("<tr class=\"del\"><td class=\"ln\">").Append(oldLine++)
							.Append("</td><td class=\"ln\"></td><td class=\"code\">").Append(Escape(line.Text)).Append("</td></tr>");
						break;
					case PatchLineKind.NoNewline:
						sb.Append("<tr><td class=\"ln\"></td><td class=\"ln\"></td><td class=\"code\">")
							.Append(Escape(line.Text)).Append("</td></tr>");
						break;
					default:
						sb.Append("<tr><td class=\"ln\">").Append(oldLine++)
							.Append("</td><td class=\"ln\">").Append(newLine++)
							.Append("</td><td class=\"code\">").Append(Escape(line.Text)).Append("</td></tr>");
						break;
					}
				}
			}
			sb.Append("</table></div>");
			return sb.ToString();
		}

		/// <summary>改动类型标签（随界面语言）。</summary>
		private static string ChangeLabel(PatchFileChange change)
		{
			switch (change)
			{
			case PatchFileChange.Added:
				return PatchStrings.T("added");
			case PatchFileChange.Deleted:
				return PatchStrings.T("deleted");
			case PatchFileChange.Renamed:
				return PatchStrings.T("renamed");
			default:
				return PatchStrings.T("modified");
			}
		}

		/// <summary>HTML 转义（补丁正文任意，必须转义后才进文档）。</summary>
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
