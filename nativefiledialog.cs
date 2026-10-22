using System;
using System.Runtime.InteropServices;
using System.Text;

public static class nativefiledialog
{
	public enum nfdresult_t
	{
		NFD_ERROR,
		NFD_OKAY,
		NFD_CANCEL
	}

	public struct nfdpathset_t
	{
		private nint buf;

		private nint indices;

		private nint count;
	}

	private const string nativeLibName = "nfd";

	private static int Utf8Size(string str)
	{
		return str.Length * 4 + 1;
	}

	private unsafe static byte* Utf8EncodeNullable(string str)
	{
		if (str == null)
		{
			return null;
		}
		int num = Utf8Size(str);
		byte* ptr = (byte*)Marshal.AllocHGlobal(num);
		fixed (char* chars = str)
		{
			Encoding.UTF8.GetBytes(chars, (str != null) ? (str.Length + 1) : 0, ptr, num);
		}
		return ptr;
	}

	private unsafe static string UTF8_ToManaged(nint s, bool freePtr = false)
	{
		if (s == IntPtr.Zero)
		{
			return null;
		}
		byte* ptr;
		for (ptr = (byte*)s; *ptr != 0; ptr++)
		{
		}
		int num = (int)(ptr - (byte*)s);
		if (num == 0)
		{
			return string.Empty;
		}
		char* ptr2 = stackalloc char[num];
		int chars = Encoding.UTF8.GetChars((byte*)s, num, ptr2, num);
		string result = new string(ptr2, 0, chars);
		if (freePtr)
		{
			free(s);
		}
		return result;
	}

	private unsafe static void free(nint ptr)
	{
		NativeMemory.Free((void*)ptr);
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_OpenDialog")]
	private unsafe static extern nfdresult_t INTERNAL_NFD_OpenDialog(byte* filterList, byte* defaultPath, out nint outPath);

	public unsafe static nfdresult_t NFD_OpenDialog(string filterList, string defaultPath, out string outPath)
	{
		byte* num = Utf8EncodeNullable(filterList);
		byte* ptr = Utf8EncodeNullable(defaultPath);
		nfdresult_t result = INTERNAL_NFD_OpenDialog(num, ptr, out var outPath2);
		Marshal.FreeHGlobal((nint)num);
		Marshal.FreeHGlobal((nint)ptr);
		outPath = UTF8_ToManaged(outPath2);
		return result;
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_OpenDialogMultiple")]
	private unsafe static extern nfdresult_t INTERNAL_NFD_OpenDialogMultiple(byte* filterList, byte* defaultPath, out nfdpathset_t outPaths);

	public unsafe static nfdresult_t NFD_OpenDialogMultiple(string filterList, string defaultPath, out nfdpathset_t outPaths)
	{
		byte* num = Utf8EncodeNullable(filterList);
		byte* ptr = Utf8EncodeNullable(defaultPath);
		nfdresult_t result = INTERNAL_NFD_OpenDialogMultiple(num, ptr, out outPaths);
		Marshal.FreeHGlobal((nint)num);
		Marshal.FreeHGlobal((nint)ptr);
		return result;
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_SaveDialog")]
	private unsafe static extern nfdresult_t INTERNAL_NFD_SaveDialog(byte* filterList, byte* defaultPath, out nint outPath);

	public unsafe static nfdresult_t NFD_SaveDialog(string filterList, string defaultPath, out string outPath)
	{
		byte* num = Utf8EncodeNullable(filterList);
		byte* ptr = Utf8EncodeNullable(defaultPath);
		nfdresult_t result = INTERNAL_NFD_SaveDialog(num, ptr, out var outPath2);
		Marshal.FreeHGlobal((nint)num);
		Marshal.FreeHGlobal((nint)ptr);
		outPath = UTF8_ToManaged(outPath2);
		return result;
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_PickFolder")]
	private unsafe static extern nfdresult_t INTERNAL_NFD_PickFolder(byte* defaultPath, out nint outPath);

	public unsafe static nfdresult_t NFD_PickFolder(string defaultPath, out string outPath)
	{
		byte* num = Utf8EncodeNullable(defaultPath);
		nfdresult_t result = INTERNAL_NFD_PickFolder(num, out var outPath2);
		Marshal.FreeHGlobal((nint)num);
		outPath = UTF8_ToManaged(outPath2, freePtr: true);
		return result;
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_GetError")]
	private static extern nint INTERNAL_NFD_GetError();

	public static string NFD_GetError()
	{
		return UTF8_ToManaged(INTERNAL_NFD_GetError());
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl)]
	public static extern nint NFD_PathSet_GetCount(ref nfdpathset_t pathset);

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl, EntryPoint = "NFD_PathSet_GetPath")]
	private static extern nint INTERNAL_NFD_PathSet_GetPath(ref nfdpathset_t pathset, nint index);

	public static string NFD_PathSet_GetPath(ref nfdpathset_t pathset, nint index)
	{
		return UTF8_ToManaged(INTERNAL_NFD_PathSet_GetPath(ref pathset, index));
	}

	[DllImport("nfd", CallingConvention = CallingConvention.Cdecl)]
	public static extern void NFD_PathSet_Free(ref nfdpathset_t pathset);
}
