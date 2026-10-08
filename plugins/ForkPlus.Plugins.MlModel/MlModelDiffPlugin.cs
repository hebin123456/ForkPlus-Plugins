using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.MlModel
{
	/// <summary>
	/// 机器学习模型对比视图插件：认领 .onnx / .safetensors / .gguf，命中后由
	/// <see cref="MlModelDiffView"/> 把两侧模型解析成统一的键路径行（元数据与张量），
	/// 按键路径取并集得到「相同 / 已变 / 仅左 / 仅右」四类，并有 Metadata / Tensors 两种视图。
	///
	/// 路由：本插件认领的三种格式都是**二进制**，而宿主只对**二进制差异**查询插件路由
	/// （用户绑定 &gt; 精确扩展名认领，不走通配兜底），因此选中模型文件的 diff 必命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为模型改的往往是「哪个张量的 shape / dtype / 偏移变了」——
	/// 逐字节的二进制 diff 对模型文件几乎读不出来。
	///
	/// 零第三方依赖：ONNX 的 protobuf 线格式、SafeTensors、GGUF 全在插件内自解析
	/// （详见 <see cref="MlModelParser"/>、<see cref="MlModelDiff"/>），
	/// 随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class MlModelDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.mlmodel";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "ML Model Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "ML Model Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Machine learning model compare view plugin: claims .onnx / .safetensors / .gguf, parses both sides into unified key-path rows (metadata and tensors) and marks each row as same / changed / left only / right only.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, MlModelStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, MlModelStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的模型扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[3]
		{
			".onnx",
			".safetensors",
			".gguf"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new MlModelDiffView();
		}
	}
}
