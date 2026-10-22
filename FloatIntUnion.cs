using System.Runtime.InteropServices;

namespace Terraria;

[StructLayout(LayoutKind.Explicit)]
public struct FloatIntUnion
{
	[FieldOffset(0)]
	public float Float;
	[FieldOffset(0)]
	public int Int;

	public bool this[int bit] {
		get {
			return (Int & (1 << bit)) != 0;
		}
		set {
			int num = 1 << bit;
			if (value)
				Int |= num;
			else
				Int &= ~num;
		}
	}

	public static implicit operator FloatIntUnion(float value)
	{
		FloatIntUnion result = default(FloatIntUnion);
		result.Float = value;
		return result;
	}

	public static implicit operator float(FloatIntUnion union)
	{
		return union.Float;
	}

	public static implicit operator FloatIntUnion(int value)
	{
		FloatIntUnion result = default(FloatIntUnion);
		result.Int = value;
		return result;
	}

	public static implicit operator int(FloatIntUnion union)
	{
		return union.Int;
	}
}
