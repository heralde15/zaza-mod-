using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Terraria.Testing;

public class PacketHistory
{
	private struct PacketView(int offset, int length, DateTime time)
	{
		public static readonly PacketView Empty = new PacketView(0, 0, DateTime.Now);

		public readonly int Offset = offset;

		public readonly int Length = length;

		public readonly DateTime Time = time;
	}

	private byte[] _buffer;

	private PacketView[] _packets;

	private int _bufferPosition;

	private int _historyPosition;

	public PacketHistory(int historySize = 100, int bufferSize = 65535)
	{
	}

	[Conditional("DEBUG")]
	public void Record(byte[] buffer, int offset, int length)
	{
		length = Math.Max(0, length);
		int offset2 = AppendPacket(length).Offset;
		Buffer.BlockCopy(buffer, offset, _buffer, offset2, length);
	}

	private PacketView AppendPacket(int size)
	{
		int num = _bufferPosition;
		if (num + size > _buffer.Length)
		{
			num = 0;
		}
		PacketView packetView = new PacketView(num, size, DateTime.Now);
		_packets[_historyPosition] = packetView;
		_historyPosition = (_historyPosition + 1) % _packets.Length;
		_bufferPosition = num + size;
		return packetView;
	}

	[Conditional("DEBUG")]
	public void Dump(string reason)
	{
		byte[] dst = new byte[_buffer.Length];
		Buffer.BlockCopy(_buffer, _bufferPosition, dst, 0, _buffer.Length - _bufferPosition);
		Buffer.BlockCopy(_buffer, 0, dst, _buffer.Length - _bufferPosition, _bufferPosition);
		StringBuilder stringBuilder = new StringBuilder();
		int num = 1;
		for (int i = 0; i < _packets.Length; i++)
		{
			PacketView packetView = _packets[(i + _historyPosition) % _packets.Length];
			if (packetView.Offset == 0 && packetView.Length == 0)
			{
				continue;
			}
			InlineArray5<object> buffer = default(InlineArray5<object>);
			buffer[0] = num++;
			buffer[1] = packetView.Offset;
			buffer[2] = packetView.Length;
			buffer[3] = packetView.Time;
			buffer[4] = _buffer[packetView.Offset];
			stringBuilder.Append(string.Format("Packet {0} [Assumed MessageID: {4}, Size: {2}, Buffer Position: {1}, Timestamp: {3:G}]\r\n", (ReadOnlySpan<object?>)buffer));
			for (int j = 0; j < packetView.Length; j++)
			{
				stringBuilder.Append(_buffer[packetView.Offset + j].ToString("X2") + " ");
				if (j % 16 == 15 && j != _packets.Length - 1)
				{
					stringBuilder.Append("\r\n");
				}
			}
			stringBuilder.Append("\r\n\r\n");
		}
		stringBuilder.Append(reason);
		Directory.CreateDirectory(Path.Combine(Main.SavePath, "NetDump"));
		File.WriteAllText(Path.Combine(Main.SavePath, "NetDump", CreateDumpFileName()), stringBuilder.ToString());
	}

	private string CreateDumpFileName()
	{
		DateTime dateTime = DateTime.Now.ToLocalTime();
		InlineArray4<object> buffer = default(InlineArray4<object>);
		buffer[0] = (Main.dedServ ? "TerrariaServer" : "Terraria");
		buffer[1] = Main.versionNumber;
		buffer[2] = dateTime.ToString("MM-dd-yy_HH-mm-ss-ffff", CultureInfo.InvariantCulture);
		buffer[3] = Thread.CurrentThread.ManagedThreadId;
		return string.Format("Net_{0}_{1}_{2}_{3}.txt", (ReadOnlySpan<object?>)buffer);
	}

	[Conditional("DEBUG")]
	private void InitializeBuffer(int historySize, int bufferSize)
	{
		_packets = new PacketView[historySize];
		_buffer = new byte[bufferSize];
	}
}
