using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.MlModel
{
	/// <summary>
	/// 一行的类别：模型元数据（Meta，如 producer / 算子直方图 / KV 配置）或张量条目
	/// （Tensor，如 node / initializer / tensor.shape）。只影响视图分模式展示，不参与 diff 对齐。
	/// </summary>
	internal enum MlRowKind
	{
		Meta,

		Tensor
	}

	/// <summary>统一模型中的一行：键路径 + 标量值。三种格式各自产出，键路径即 diff 的对齐键。</summary>
	internal sealed class MlRow
	{
		/// <summary>键路径（如 "tensor.ln.weight.shape"、"node[3]"、"meta.general.architecture"）。</summary>
		internal string Path = string.Empty;

		/// <summary>标量值（展示与比较都用字符串形式）。</summary>
		internal string Value = string.Empty;

		internal MlRowKind Kind;
	}

	/// <summary>一次解析的结果：格式徽章 + 键路径行列表 + 张量数 / 参数量统计。</summary>
	internal sealed class MlModel
	{
		/// <summary>格式徽章："ONNX" / "SafeTensors" / "GGUF"。</summary>
		internal string Format = "?";

		internal List<MlRow> Rows = new List<MlRow>();

		/// <summary>张量条目数（ONNX 为 initializer 数；状态行 "tensors" 用）。</summary>
		internal int TensorCount;

		/// <summary>参数量（各张量 shape 乘积之和，饱和到 long.MaxValue）；状态行按 N0 千分位展示。</summary>
		internal long ParamCount;
	}

	internal enum MlDiffState
	{
		Same,

		Changed,

		LeftOnly,

		RightOnly
	}

	/// <summary>diff 后的一行：同一键路径在旧 / 新两侧的值与四态。</summary>
	internal sealed class MlDiffRow
	{
		internal string Path = string.Empty;

		internal string LeftValue;

		internal string RightValue;

		internal bool LeftPresent;

		internal bool RightPresent;

		internal MlRowKind Kind;

		internal MlDiffState State;
	}

	internal sealed class MlDiffResult
	{
		internal List<MlDiffRow> Rows = new List<MlDiffRow>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// 模型语义 diff：两侧各产出「键路径 → 值」行后按键路径取并集，逐键判
	/// 相同 / 已变 / 仅左 / 仅右（值按 Ordinal 逐字符比较）。
	///
	/// 左侧行序为主序、右侧行补在其后（仅右的键保持右文件中的出现次序）；
	/// 重复键路径只取首条。diff 一次算全（Meta + Tensor），视图的模式切换只做展示过滤。
	/// </summary>
	internal static class MlModelDiff
	{
		internal static MlDiffResult Compute(MlModel left, MlModel right)
		{
			MlDiffResult result = new MlDiffResult();
			Dictionary<string, MlRow> rightMap = new Dictionary<string, MlRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (MlRow row in right.Rows)
				{
					if (!rightMap.ContainsKey(row.Path))
					{
						rightMap.Add(row.Path, row);
					}
				}
			}
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			if (left != null)
			{
				foreach (MlRow row in left.Rows)
				{
					if (!seen.Add(row.Path))
					{
						continue;
					}
					bool hasRight = rightMap.TryGetValue(row.Path, out MlRow rightRow);
					MlDiffState state = !hasRight
						? MlDiffState.LeftOnly
						: (string.Equals(row.Value, rightRow.Value, StringComparison.Ordinal) ? MlDiffState.Same : MlDiffState.Changed);
					Add(result, row.Path, row.Value, true, hasRight ? rightRow.Value : null, hasRight, row.Kind, state);
				}
			}
			if (right != null)
			{
				foreach (MlRow row in right.Rows)
				{
					if (!seen.Add(row.Path))
					{
						continue;
					}
					Add(result, row.Path, null, false, row.Value, true, row.Kind, MlDiffState.RightOnly);
				}
			}
			return result;
		}

		private static void Add(MlDiffResult result, string path, string leftValue, bool leftPresent, string rightValue, bool rightPresent, MlRowKind kind, MlDiffState state)
		{
			result.Rows.Add(new MlDiffRow
			{
				Path = path,
				LeftValue = leftValue,
				LeftPresent = leftPresent,
				RightValue = rightValue,
				RightPresent = rightPresent,
				Kind = kind,
				State = state
			});
			switch (state)
			{
			case MlDiffState.Changed:
				result.Changed++;
				break;
			case MlDiffState.LeftOnly:
				result.LeftOnly++;
				break;
			case MlDiffState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}
