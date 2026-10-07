using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Video
{
	/// <summary>
	/// 视频对比视图插件：认领主流视频容器（.mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg /
	/// .mpeg / .wmv / .flv），命中后由 <see cref="VideoDiffView"/> 并排呈现元数据、关键帧帧条
	/// 与单帧像素差异，差异逐行标注。
	///
	/// 解码走共享核心 <c>ForkPlus.Plugins.Media</c>（FFmpeg.AutoGen 动态绑定，只解码不编码）。
	/// 300 MB 阈值取「设计文档 §5 方案 A」：CanHandle 一律放行，进视图后单侧超阈值只给提示、不渲染媒体。
	///
	/// 另实现 <see cref="IPluginMetadata"/> 向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class VideoDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.video";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用）。</summary>
		public string DisplayNameKey => "Video Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Video Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Video compare view plugin: claims .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv, decodes with FFmpeg and shows metadata, keyframe filmstrip and single-frame pixel difference (honours the host 'highlight changed pixels' preference) side by side, with row-level difference labels; a Playback mode plays either side (play / pause + seek, hardware decoding preferred with silent fallback to software). Files larger than 300 MB per side are not previewed.";

		/// <summary>按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, VideoStrings.DisplayNames, DisplayName);
		}

		/// <summary>按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, VideoStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与 PDF / Office / Archive / Font / Audio 插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。与音频插件无重叠（.m4v 视频 / .m4a 音频）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[10]
		{
			".mp4",
			".mkv",
			".mov",
			".webm",
			".avi",
			".m4v",
			".mpg",
			".mpeg",
			".wmv",
			".flv"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受（阈值 A）。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new VideoDiffView();
		}
	}
}
