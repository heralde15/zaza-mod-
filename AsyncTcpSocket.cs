using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.Localization;
using Terraria.Net;
using Terraria.Net.Sockets;

public class AsyncTcpSocket : ISocket, IAsyncDisposable
{
	private readonly TcpClient _tcpClient;

	private NetworkStream _networkStream;

	private TcpListener _tcpListener;

	private SocketConnectionAccepted _clientAcceptedCallback;

	private RemoteAddress _remoteAddress;

	private volatile bool _isListening;

	private int _isDisposed;

	private TaskCompletionSource<bool> _listenerStoppedTcs;

	private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _receiveLock = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _operationsCts = new CancellationTokenSource();

	private readonly CancellationTokenSource _listenerCts = new CancellationTokenSource();

	private Task _connectTask;

	public AsyncTcpSocket()
	{
		_tcpClient = new TcpClient
		{
			NoDelay = true
		};
		try
		{
			_tcpClient.Client.LingerState = new LingerOption(enable: false, 0);
		}
		catch
		{
		}
	}

	public AsyncTcpSocket(TcpClient client)
	{
		_tcpClient = client;
		_tcpClient.NoDelay = true;
		try
		{
			_tcpClient.Client.LingerState = new LingerOption(enable: false, 0);
		}
		catch
		{
		}
		_networkStream = _tcpClient.GetStream();
		if (_tcpClient.Client.RemoteEndPoint is IPEndPoint remoteEndPoint)
		{
			_remoteAddress = new TcpAddress(remoteEndPoint.Address, remoteEndPoint.Port);
		}
	}

	void ISocket.Connect(RemoteAddress address)
	{
		Task connectTask = _connectTask;
		if (connectTask == null || connectTask.IsCompleted)
		{
			TcpAddress tcpAddress = (TcpAddress)address;
			_connectTask = ConnectAsync(tcpAddress);
		}
	}

	public async Task ConnectAsync(TcpAddress address)
	{
		if (_tcpClient.Connected)
		{
			return;
		}
		for (int attempt = 1; attempt <= 2; attempt++)
		{
			try
			{
				await _tcpClient.ConnectAsync(address.Address, address.Port).ConfigureAwait(continueOnCapturedContext: false);
				_remoteAddress = address;
				_networkStream = _tcpClient.GetStream();
				break;
			}
			catch
			{
				if (attempt < 2)
				{
					await Task.Delay(TimeSpan.FromSeconds(15.0)).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
	}

	bool ISocket.IsConnected()
	{
		return _tcpClient.Connected;
	}

	void ISocket.Close()
	{
		CloseAsync();
	}

	RemoteAddress ISocket.GetRemoteAddress()
	{
		return _remoteAddress;
	}

	bool ISocket.IsDataAvailable()
	{
		if (_tcpClient.Connected && _networkStream != null)
		{
			return _networkStream.DataAvailable;
		}
		return false;
	}

	void ISocket.AsyncSend(byte[] buffer, int offset, int count, SocketSendCallback callback, object state)
	{
		SendAsync(buffer, offset, count).ContinueWith(delegate
		{
			callback?.Invoke(state);
		}, TaskScheduler.Default);
	}

	void ISocket.AsyncReceive(byte[] buffer, int offset, int count, SocketReceiveCallback callback, object state)
	{
		ReceiveAsync(buffer, offset, count).ContinueWith(delegate(Task<int> t)
		{
			callback?.Invoke(state, t.IsCompletedSuccessfully ? t.Result : 0);
		}, TaskScheduler.Default);
	}

	void ISocket.SendQueuedPackets()
	{
	}

	bool ISocket.StartListening(SocketConnectionAccepted callback)
	{
		_isListening = true;
		_clientAcceptedCallback = callback;
		IPAddress listenAddress = IPAddress.Any;
		if (Program.LaunchParameters.TryGetValue("-ip", out var ipValue))
		{
			IPAddress.TryParse(ipValue, out listenAddress);
		}
		_tcpListener = new TcpListener(listenAddress, Netplay.ListenPort);
		_tcpListener.Start();
		_listenerStoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		ListenLoopAsync();
		return true;
	}

	void ISocket.StopListening()
	{
		_isListening = false;
		_listenerCts.Cancel();
		try
		{
			_tcpListener?.Stop();
		}
		catch
		{
		}
		_listenerStoppedTcs?.TrySetResult(result: true);
	}

	public Task CloseAsync()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
		{
			return Task.CompletedTask;
		}
		_isListening = false;
		_listenerStoppedTcs?.TrySetResult(result: true);
		_operationsCts.Cancel();
		_listenerCts.Cancel();
		try
		{
			_tcpListener?.Stop();
		}
		catch
		{
		}
		try
		{
			if (_tcpClient.Connected)
			{
				_tcpClient.Client.Shutdown(SocketShutdown.Both);
			}
		}
		catch
		{
		}
		try
		{
			_networkStream?.Dispose();
		}
		catch
		{
		}
		try
		{
			_tcpClient?.Dispose();
		}
		catch
		{
		}
		_sendLock.Dispose();
		_receiveLock.Dispose();
		_operationsCts.Dispose();
		_listenerCts.Dispose();
		GC.SuppressFinalize(this);
		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await CloseAsync().ConfigureAwait(continueOnCapturedContext: false);
	}

	private bool CanSend()
	{
		if (_isDisposed == 0 && _tcpClient.Connected)
		{
			return _networkStream?.CanWrite ?? false;
		}
		return false;
	}

	private bool CanReceive()
	{
		if (_isDisposed == 0 && _tcpClient.Connected)
		{
			return _networkStream?.CanRead ?? false;
		}
		return false;
	}

	private async Task SendAsync(byte[] buffer, int offset, int count)
	{
		if (_connectTask != null)
		{
			try
			{
				await _connectTask.ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				return;
			}
		}
		if (!CanSend())
		{
			return;
		}
		await _sendLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (CanSend())
			{
				await _networkStream.WriteAsync(buffer.AsMemory(offset, count)).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch
		{
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
	{
		if (_connectTask != null)
		{
			try
			{
				await _connectTask.ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				return 0;
			}
		}
		if (!CanReceive())
		{
			return 0;
		}
		await _receiveLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (!CanReceive())
			{
				return 0;
			}
			return await _networkStream.ReadAsync(buffer.AsMemory(offset, count)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			return 0;
		}
		finally
		{
			_receiveLock.Release();
		}
	}

	private async Task ListenLoopAsync()
	{
		try
		{
			while (_isListening && !Netplay.Disconnect)
			{
				Task<TcpClient> acceptTask = _tcpListener.AcceptTcpClientAsync();
				if (await Task.WhenAny(acceptTask, Task.Delay(-1, _listenerCts.Token)) == acceptTask)
				{
					TcpClient client = acceptTask.Result;
					try
					{
						client.Client.LingerState = new LingerOption(enable: false, 0);
					}
					catch
					{
					}
					ISocket socket = new AsyncTcpSocket(client);
					Console.WriteLine(Language.GetTextValue("Net.ClientConnecting", socket.GetRemoteAddress()));
					_clientAcceptedCallback?.Invoke(socket);
					continue;
				}
				break;
			}
		}
		catch
		{
		}
		finally
		{
			try
			{
				_tcpListener?.Stop();
			}
			catch
			{
			}
			_listenerStoppedTcs?.TrySetResult(result: true);
		}
	}
}
