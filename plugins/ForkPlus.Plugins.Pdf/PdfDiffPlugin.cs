using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pdf
{
	/// <summary>
	/// PDF 对比视图插件：声明认领 <c>.pdf</c>，命中后由 <see cref="PdfDiffView"/> 把旧 / 新
	/// 两个 PDF 逐页渲染成左右两栏并排对比。
	///
	/// PDF 渲染用 MIT 许可的 Docnet.Core（底层 PDFium）；原生库按平台随插件包分发，
	/// 由 <see cref="PdfNativeLibrary"/> 在运行期从插件目录解析。
	/// </summary>
	public sealed class PdfDiffPlugin : IDiffViewPlugin
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.pdf";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "PDF";

		/// <summary>高于内置通配兜底（Hex，0）；与示例插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".pdf" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new PdfDiffView();
		}
	}
}