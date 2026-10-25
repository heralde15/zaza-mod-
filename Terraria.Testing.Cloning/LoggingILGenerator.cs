using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace Terraria.Testing.Cloning;

internal sealed class LoggingILGenerator
{
	private readonly ILGenerator il;

	public readonly StringBuilder log;

	public LoggingILGenerator(ILGenerator il, string header)
	{
		this.il = il;
		log = new StringBuilder();
		log.AppendLine(header);
	}

	public LocalBuilder DeclareLocal(Type type)
	{
		LocalBuilder localBuilder = il.DeclareLocal(type);
		log.AppendLine(".local [" + localBuilder.LocalIndex + "] " + type.FullName);
		return localBuilder;
	}

	public void Emit(OpCode opcode)
	{
		log.AppendLine("  " + opcode);
		il.Emit(opcode);
	}

	public void Emit(OpCode opcode, MethodInfo method)
	{
		StringBuilder stringBuilder = log;
		InlineArray6<object> buffer = default(InlineArray6<object>);
		buffer[0] = "  ";
		buffer[1] = opcode;
		buffer[2] = " ";
		buffer[3] = ((method.DeclaringType != null) ? method.DeclaringType.Name : "dynamic");
		buffer[4] = "::";
		buffer[5] = method.Name;
		stringBuilder.AppendLine(string.Concat((ReadOnlySpan<object?>)buffer));
		il.Emit(opcode, method);
	}

	public void Emit(OpCode opcode, FieldInfo field)
	{
		StringBuilder stringBuilder = log;
		InlineArray8<object> buffer = default(InlineArray8<object>);
		buffer[0] = "  ";
		buffer[1] = opcode;
		buffer[2] = " ";
		buffer[3] = field.FieldType.Name;
		buffer[4] = " ";
		buffer[5] = field.DeclaringType.Name;
		buffer[6] = "::";
		buffer[7] = field.Name;
		stringBuilder.AppendLine(string.Concat((ReadOnlySpan<object?>)buffer));
		il.Emit(opcode, field);
	}

	public void Emit(OpCode opcode, LocalBuilder local)
	{
		StringBuilder stringBuilder = log;
		InlineArray5<object> buffer = default(InlineArray5<object>);
		buffer[0] = "  ";
		buffer[1] = opcode;
		buffer[2] = " [";
		buffer[3] = local.LocalIndex;
		buffer[4] = "]";
		stringBuilder.AppendLine(string.Concat((ReadOnlySpan<object?>)buffer));
		il.Emit(opcode, local);
	}

	public void Emit(OpCode opcode, Type type)
	{
		StringBuilder stringBuilder = log;
		InlineArray4<object> buffer = default(InlineArray4<object>);
		buffer[0] = "  ";
		buffer[1] = opcode;
		buffer[2] = " ";
		buffer[3] = type.FullName;
		stringBuilder.AppendLine(string.Concat((ReadOnlySpan<object?>)buffer));
		il.Emit(opcode, type);
	}

	public Label DefineLabel()
	{
		return il.DefineLabel();
	}

	public void MarkLabel(Label label)
	{
		log.AppendLine("label_" + label.GetHashCode() + ":");
		il.MarkLabel(label);
	}

	public void Emit(OpCode opcode, Label label)
	{
		StringBuilder stringBuilder = log;
		InlineArray4<object> buffer = default(InlineArray4<object>);
		buffer[0] = "  ";
		buffer[1] = opcode;
		buffer[2] = " label_";
		buffer[3] = label.GetHashCode();
		stringBuilder.AppendLine(string.Concat((ReadOnlySpan<object?>)buffer));
		il.Emit(opcode, label);
	}

	public string GetLog()
	{
		return log.ToString();
	}
}
