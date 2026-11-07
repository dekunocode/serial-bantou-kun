using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serial_TCP_IP_Monitor_Avalonia.Models;
using Serial_TCP_IP_Monitor_Avalonia.Protocol;
using Serial_TCP_IP_Monitor_Avalonia.Services;

namespace Serial_TCP_IP_Monitor_Avalonia.ViewModels;

public enum CommunicationMode
{
    Serial,
    Tcp
}

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogEntries = 2000;
    private static readonly TimeSpan AutoFrameFlushDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LogRenderInterval = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(180);

    private readonly SerialPortService _serialService;
    private readonly TcpCommunicationService _tcpService;
    private readonly SerialPortDiscoveryService _portDiscoveryService;
    private readonly SettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ReceiveFrameAssembler _frameAssembler = new();
    private readonly DispatcherTimer _autoFrameFlushTimer;
    private readonly DispatcherTimer _logRenderTimer;
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly List<LogEntry> _allLogs = [];
    private readonly Queue<LogEntry> _pendingLogs = [];
    private readonly List<string> _commandHistory = [];
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Dictionary<string, byte[]> _transmitDelimiters = new(StringComparer.Ordinal)
    {
        ["None"] = [],
        ["CR"] = [0x0D],
        ["LF"] = [0x0A],
        ["CRLF"] = [0x0D, 0x0A]
    };

    private CommunicationMode _mode;
    private Decoder _streamDecoder = Encoding.UTF8.GetDecoder();
    private int _historyIndex;
    private bool _isInitialized;
    private bool _disposed;

    public MainViewModel(
        SerialPortService serialService,
        TcpCommunicationService tcpService,
        SerialPortDiscoveryService portDiscoveryService,
        SettingsService settingsService,
        IFileDialogService fileDialogService)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _serialService = serialService;
        _tcpService = tcpService;
        _portDiscoveryService = portDiscoveryService;
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;

        BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400];
        ParityOptions = [Parity.None, Parity.Odd, Parity.Even, Parity.Mark, Parity.Space];
        DataBitsOptions = [5, 6, 7, 8];
        StopBitsOptions = [StopBits.One, StopBits.Two, StopBits.OnePointFive];
        HandshakeOptions = [Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff];
        EncodingOptions =
        [
            new EncodingOption("UTF-8", new UTF8Encoding(false)),
            new EncodingOption("ASCII", Encoding.ASCII),
            new EncodingOption("Shift-JIS", Encoding.GetEncoding(932))
        ];
        ReceiveDelimiterOptions = Enum.GetValues<ReceiveDelimiterMode>();
        TransmitDelimiterOptions = [.. _transmitDelimiters.Keys];
        ViewModeOptions = ["Text", "Hex"];

        _autoFrameFlushTimer = new DispatcherTimer { Interval = AutoFrameFlushDelay };
        _autoFrameFlushTimer.Tick += (_, _) => FlushAutoFrame();
        _logRenderTimer = new DispatcherTimer { Interval = LogRenderInterval };
        _logRenderTimer.Tick += (_, _) => FlushPendingLogs();
        _searchDebounceTimer = new DispatcherTimer { Interval = SearchDebounceDelay };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            RebuildVisibleLogs();
        };

        _serialService.DataReceived += DispatchReceivedData;
        _serialService.ErrorOccurred += message => DispatchUi(() => HandleCommunicationError(message));
        _serialService.WarningOccurred += message => DispatchUi(() => AppendLog(LogCategory.Error, message));
        _tcpService.DataReceived += DispatchReceivedData;
        _tcpService.ErrorOccurred += message => DispatchUi(() => HandleCommunicationError(message));

        LoadSettings(_settingsService.Load());
        ResetStreamDecoder();
        SetStatus("準備完了");
    }

    public IReadOnlyList<int> BaudRates { get; }
    public IReadOnlyList<Parity> ParityOptions { get; }
    public IReadOnlyList<int> DataBitsOptions { get; }
    public IReadOnlyList<StopBits> StopBitsOptions { get; }
    public IReadOnlyList<Handshake> HandshakeOptions { get; }
    public IReadOnlyList<EncodingOption> EncodingOptions { get; }
    public IReadOnlyList<ReceiveDelimiterMode> ReceiveDelimiterOptions { get; }
    public IReadOnlyList<string> TransmitDelimiterOptions { get; }
    public IReadOnlyList<string> ViewModeOptions { get; }
    public ObservableCollection<SerialPortInfo> Ports { get; } = [];
    public ObservableCollection<LogEntry> VisibleLogs { get; } = [];

    public CommunicationMode Mode
    {
        get => _mode;
        private set
        {
            if (!SetProperty(ref _mode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsSerialMode));
            OnPropertyChanged(nameof(IsTcpMode));
            OnPropertyChanged(nameof(RtsManualEnabled));
            ClearReceiveState();
        }
    }

    public bool IsSerialMode
    {
        get => Mode == CommunicationMode.Serial;
        set
        {
            if (value && CanEditConnection)
            {
                Mode = CommunicationMode.Serial;
            }
        }
    }

    public bool IsTcpMode
    {
        get => Mode == CommunicationMode.Tcp;
        set
        {
            if (value && CanEditConnection)
            {
                Mode = CommunicationMode.Tcp;
            }
        }
    }

    public bool CanEditConnection => !IsConnected && !IsConnecting;
    public bool CanSend => IsConnected && !IsSending;
    public bool RtsManualEnabled => CanEditConnection && IsSerialMode && !UsesRtsFlowControl(SelectedHandshake);
    public string ConnectionLabel => IsConnected ? "接続中" : IsConnecting ? "接続中…" : StatusIsError ? "エラー" : "未接続";
    public string ConnectButtonText => IsConnected ? "切断" : IsConnecting ? "接続中…" : "接続";

    [ObservableProperty] private SerialPortInfo? _selectedPort;
    [ObservableProperty] private int _selectedBaudRate = 9600;
    [ObservableProperty] private Parity _selectedParity = Parity.None;
    [ObservableProperty] private int _selectedDataBits = 8;
    [ObservableProperty] private StopBits _selectedStopBits = StopBits.One;
    [ObservableProperty] private Handshake _selectedHandshake = Handshake.None;
    [ObservableProperty] private bool _dtrEnable = true;
    [ObservableProperty] private bool _rtsEnable = true;
    [ObservableProperty] private string _tcpHost = "127.0.0.1";
    [ObservableProperty] private string _tcpPortText = "23";
    [ObservableProperty] private EncodingOption _selectedEncoding = null!;
    [ObservableProperty] private ReceiveDelimiterMode _selectedReceiveDelimiter = ReceiveDelimiterMode.Auto;
    [ObservableProperty] private string _selectedTransmitDelimiter = "CRLF";
    [ObservableProperty] private string _receiveViewMode = "Text";
    [ObservableProperty] private string _sendMode = "Text";
    [ObservableProperty] private bool _showDelimiterTags = true;
    [ObservableProperty] private string _sendText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _filterReceive = true;
    [ObservableProperty] private bool _filterTransmit = true;
    [ObservableProperty] private bool _filterSystem = true;
    [ObservableProperty] private bool _filterError = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _isRefreshingPorts;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private long _receivedByteCount;
    [ObservableProperty] private long _transmittedByteCount;

    public string ReceivedBytesText => $"RX {ReceivedByteCount:N0} B";
    public string TransmittedBytesText => $"TX {TransmittedByteCount:N0} B";

    partial void OnSelectedHandshakeChanged(Handshake value) => OnPropertyChanged(nameof(RtsManualEnabled));
    partial void OnSelectedEncodingChanged(EncodingOption value) => ClearReceiveState();
    partial void OnSelectedReceiveDelimiterChanged(ReceiveDelimiterMode value) => ClearReceiveState();

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnFilterReceiveChanged(bool value) => RebuildVisibleLogs();
    partial void OnFilterTransmitChanged(bool value) => RebuildVisibleLogs();
    partial void OnFilterSystemChanged(bool value) => RebuildVisibleLogs();
    partial void OnFilterErrorChanged(bool value) => RebuildVisibleLogs();

    partial void OnIsConnectedChanged(bool value) => NotifyConnectionStateChanged();
    partial void OnIsConnectingChanged(bool value) => NotifyConnectionStateChanged();
    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
        SendCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsRefreshingPortsChanged(bool value) => RefreshPortsCommand.NotifyCanExecuteChanged();
    partial void OnStatusIsErrorChanged(bool value) => OnPropertyChanged(nameof(ConnectionLabel));
    partial void OnReceivedByteCountChanged(long value) => OnPropertyChanged(nameof(ReceivedBytesText));
    partial void OnTransmittedByteCountChanged(long value) => OnPropertyChanged(nameof(TransmittedBytesText));

    [RelayCommand(CanExecute = nameof(CanConnectOrDisconnect))]
    private async Task ConnectOrDisconnectAsync()
    {
        if (IsConnected)
        {
            DisconnectCurrentConnection();
            return;
        }
        if (IsTcpMode)
        {
            await ConnectTcpAsync();
        }
        else
        {
            ConnectSerial();
        }
    }

    private bool CanConnectOrDisconnect() => !IsConnecting;

    public async Task InitializeAsync()
    {
        if (_isInitialized || _disposed)
        {
            return;
        }

        _isInitialized = true;
        await RefreshPortsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshPorts))]
    private async Task RefreshPortsAsync()
    {
        if (_disposed)
        {
            return;
        }

        var currentPortName = SelectedPort?.PortName;
        IsRefreshingPorts = true;
        SetStatus("シリアルポートを検索しています…");
        try
        {
            var ports = await _portDiscoveryService.GetPortsAsync(_lifetimeCts.Token);
            if (_disposed)
            {
                return;
            }

            Ports.Clear();
            foreach (var port in ports)
            {
                Ports.Add(port);
            }
            SelectedPort = Ports.FirstOrDefault(port => string.Equals(port.PortName, currentPortName, StringComparison.OrdinalIgnoreCase))
                ?? Ports.FirstOrDefault();
            if (Ports.Count == 0)
            {
                SetStatus("利用可能なシリアルポートがありません");
            }
            else
            {
                SetStatus($"シリアルポートを{Ports.Count}件検出しました");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"ポート一覧の取得に失敗しました: {ex.Message}");
            SetStatus("ポート一覧を取得できませんでした", true);
        }
        finally
        {
            IsRefreshingPorts = false;
        }
    }

    private bool CanRefreshPorts() => !_disposed && !IsRefreshingPorts && CanEditConnection;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var input = SendText;
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }
        if (!TryCreateTransmitBytes(input, out var data, out var error))
        {
            AppendLog(LogCategory.Error, error);
            SetStatus(error, true);
            return;
        }

        var displayText = SendMode == "Hex" ? ControlCodeCodec.ToHex(data) : FormatTextTransmitForLog(input);

        IsSending = true;
        try
        {
            var sent = IsTcpMode ? await _tcpService.SendAsync(data) : await SendSerialAsync(data);
            if (!sent)
            {
                HandleCommunicationError("送信に失敗したため切断しました。");
                return;
            }

            TransmittedByteCount += data.Length;
            AppendLog(LogCategory.Transmit, displayText);
            if (_commandHistory.Count == 0 || !string.Equals(_commandHistory[^1], input, StringComparison.Ordinal))
            {
                _commandHistory.Add(input);
            }
            _historyIndex = _commandHistory.Count;
            if (SendText == input)
            {
                SendText = string.Empty;
            }
            SetStatus($"{data.Length:N0} バイト送信しました");
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        FlushPendingLogs();
        try
        {
            var saved = await _fileDialogService.SaveTextAsync(
                $"serial-monitor-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                _allLogs.Select(entry => entry.ToDisplayText()));
            if (saved)
            {
                SetStatus("ログを保存しました");
            }
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"ログ保存に失敗しました: {ex.Message}");
            SetStatus("ログ保存に失敗しました", true);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        _allLogs.Clear();
        _pendingLogs.Clear();
        VisibleLogs.Clear();
        _logRenderTimer.Stop();
        SetStatus("ログを消去しました");
    }

    public void NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _commandHistory.Count);
        SendText = _historyIndex == _commandHistory.Count ? string.Empty : _commandHistory[_historyIndex];
    }

    private async Task ConnectTcpAsync()
    {
        var host = TcpHost.Trim();
        if (string.IsNullOrEmpty(host) || !int.TryParse(TcpPortText, out var port) || port is < 1 or > 65535)
        {
            SetStatus("ホスト名と1～65535のポート番号を確認してください", true);
            return;
        }

        IsConnecting = true;
        SetStatus($"{host}:{port} へ接続中…");
        try
        {
            if (!await _tcpService.ConnectAsync(host, port))
            {
                SetStatus("TCP接続に失敗しました", true);
                return;
            }
            PrepareNewConnection();
            IsConnected = true;
            SetStatus($"TCP {host}:{port} に接続しました");
            AppendLog(LogCategory.System, $"TCP {host}:{port} に接続しました。");
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"TCP接続に失敗しました: {ex.Message}");
            SetStatus("TCP接続に失敗しました", true);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void ConnectSerial()
    {
        if (SelectedPort is null)
        {
            SetStatus("シリアルポートを選択してください", true);
            return;
        }
        try
        {
            _serialService.Open(SelectedPort.PortName, SelectedBaudRate, SelectedParity, SelectedDataBits,
                SelectedStopBits, SelectedHandshake, DtrEnable, RtsEnable);
            PrepareNewConnection();
            IsConnected = true;
            SetStatus($"{SelectedPort.PortName} @ {SelectedBaudRate} に接続しました");
            AppendLog(LogCategory.System, $"{SelectedPort.DisplayName} を開きました。");
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"シリアル接続に失敗しました: {ex.Message}");
            SetStatus("シリアル接続に失敗しました", true);
        }
    }

    private async Task<bool> SendSerialAsync(byte[] data)
    {
        try
        {
            await _serialService.WriteAsync(data);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"シリアル送信エラー: {ex.Message}");
            return false;
        }
    }

    private void DisconnectCurrentConnection()
    {
        IsSending = false;
        try
        {
            if (IsTcpMode)
            {
                _tcpService.Disconnect();
            }
            else if (_serialService.IsOpen)
            {
                _serialService.Close();
            }
        }
        catch (Exception ex)
        {
            AppendLog(LogCategory.Error, $"切断エラー: {ex.Message}");
        }

        IsConnected = false;
        ClearReceiveState();
        SetStatus("切断しました");
        AppendLog(LogCategory.System, "通信を切断しました。");
    }

    private void HandleCommunicationError(string message)
    {
        if (_disposed)
        {
            return;
        }
        AppendLog(LogCategory.Error, message);
        IsSending = false;
        try
        {
            _tcpService.Disconnect();
            if (_serialService.IsOpen)
            {
                _serialService.Close();
            }
        }
        catch
        {
        }
        IsConnected = false;
        IsConnecting = false;
        ClearReceiveState();
        SetStatus(message, true);
    }

    private void DispatchReceivedData(byte[] data)
    {
        if (!_disposed && data.Length > 0)
        {
            DispatchUi(() => ProcessReceivedBytes(data));
        }
    }

    private static void DispatchUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
        }
    }

    private void ProcessReceivedBytes(byte[] data)
    {
        ReceivedByteCount += data.Length;
        var frames = _frameAssembler.Append(data, SelectedReceiveDelimiter);
        foreach (var frame in frames)
        {
            AppendReceivedFrame(frame, SelectedReceiveDelimiter == ReceiveDelimiterMode.None);
        }
        if (_frameAssembler.WasTrimmed)
        {
            AppendLog(LogCategory.Error, "受信バッファが64KBを超えたため、古いデータを破棄しました。");
        }
        _autoFrameFlushTimer.Stop();
        if (SelectedReceiveDelimiter == ReceiveDelimiterMode.Auto && _frameAssembler.BufferedByteCount > 0)
        {
            _autoFrameFlushTimer.Start();
        }
    }

    private void FlushAutoFrame()
    {
        _autoFrameFlushTimer.Stop();
        var frame = _frameAssembler.FlushIncomplete(ReceiveDelimiterMode.Auto);
        if (frame is not null)
        {
            AppendReceivedFrame(frame, false);
        }
    }

    private void AppendReceivedFrame(ReceivedFrame frame, bool streaming)
    {
        var message = ReceiveViewMode == "Hex"
            ? ControlCodeCodec.ToHex(frame.Payload)
            : DecodeReceivedText(frame.Payload, streaming);
        if (ShowDelimiterTags && !string.IsNullOrEmpty(frame.DelimiterLabel))
        {
            message += frame.DelimiterLabel;
        }
        AppendLog(LogCategory.Receive, NormalizeControlCharacters(message));
    }

    private string DecodeReceivedText(byte[] data, bool streaming)
    {
        if (!streaming)
        {
            return SelectedEncoding.Encoding.GetString(data);
        }
        var charCount = _streamDecoder.GetCharCount(data, 0, data.Length, false);
        if (charCount == 0)
        {
            return string.Empty;
        }
        var characters = new char[charCount];
        _streamDecoder.GetChars(data, 0, data.Length, characters, 0, false);
        return new string(characters);
    }

    private static string NormalizeControlCharacters(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            result.Append(character switch
            {
                '\r' => "[CR]",
                '\n' => "[LF]",
                '\t' => "[TAB]",
                _ when char.IsControl(character) => $"[0x{(int)character:X2}]",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }

    private bool TryCreateTransmitBytes(string input, out byte[] data, out string validationError)
    {
        byte[] payload;
        if (SendMode == "Hex")
        {
            if (!ControlCodeCodec.TryParseHexBytes(input, out payload, out validationError))
            {
                data = [];
                return false;
            }
        }
        else
        {
            payload = ControlCodeCodec.ParseMixedText(input, SelectedEncoding.Encoding);
            validationError = string.Empty;
        }
        var delimiter = _transmitDelimiters.GetValueOrDefault(SelectedTransmitDelimiter, []);
        data = new byte[payload.Length + delimiter.Length];
        Buffer.BlockCopy(payload, 0, data, 0, payload.Length);
        Buffer.BlockCopy(delimiter, 0, data, payload.Length, delimiter.Length);
        return true;
    }

    private string FormatTextTransmitForLog(string input) =>
        SelectedTransmitDelimiter == "None" ? input : $"{input}[{SelectedTransmitDelimiter}]";

    private void AppendLog(LogCategory category, string message)
    {
        var entry = new LogEntry(DateTime.Now, category, message);
        _allLogs.Add(entry);
        _pendingLogs.Enqueue(entry);
        if (_allLogs.Count > MaxLogEntries)
        {
            var removed = _allLogs[0];
            _allLogs.RemoveAt(0);
            VisibleLogs.Remove(removed);
        }
        if (!_logRenderTimer.IsEnabled)
        {
            _logRenderTimer.Start();
        }
    }

    private void FlushPendingLogs()
    {
        while (_pendingLogs.TryDequeue(out var entry))
        {
            if (ShouldDisplay(entry))
            {
                VisibleLogs.Add(entry);
            }
        }
        _logRenderTimer.Stop();
    }

    private bool ShouldDisplay(LogEntry entry)
    {
        var categoryEnabled = entry.Category switch
        {
            LogCategory.Receive => FilterReceive,
            LogCategory.Transmit => FilterTransmit,
            LogCategory.System => FilterSystem,
            LogCategory.Error => FilterError,
            _ => true
        };
        return categoryEnabled && (string.IsNullOrWhiteSpace(SearchText)
            || entry.ToDisplayText().Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildVisibleLogs()
    {
        if (_disposed)
        {
            return;
        }
        _pendingLogs.Clear();
        _logRenderTimer.Stop();
        VisibleLogs.Clear();
        foreach (var entry in _allLogs.Where(ShouldDisplay))
        {
            VisibleLogs.Add(entry);
        }
    }

    private void PrepareNewConnection()
    {
        ReceivedByteCount = 0;
        TransmittedByteCount = 0;
        ClearReceiveState();
    }

    private void ClearReceiveState()
    {
        _autoFrameFlushTimer?.Stop();
        _frameAssembler.Clear();
        ResetStreamDecoder();
    }

    private void ResetStreamDecoder()
    {
        _streamDecoder = SelectedEncoding?.Encoding.GetDecoder() ?? Encoding.UTF8.GetDecoder();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private void NotifyConnectionStateChanged()
    {
        OnPropertyChanged(nameof(CanEditConnection));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(RtsManualEnabled));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(ConnectButtonText));
        ConnectOrDisconnectCommand.NotifyCanExecuteChanged();
        RefreshPortsCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
    }

    private void LoadSettings(AppSettings settings)
    {
        SelectedBaudRate = 9600;
        SelectedParity = ParseEnum(settings.SerialParity, Parity.None);
        SelectedDataBits = settings.SerialDataBits is >= 5 and <= 8 ? settings.SerialDataBits : 8;
        SelectedStopBits = ParseEnum(settings.SerialStopBits, StopBits.One);
        SelectedHandshake = ParseEnum(settings.SerialHandshake, Handshake.None);
        DtrEnable = settings.SerialDtrEnable;
        RtsEnable = settings.SerialRtsEnable;
        SelectedEncoding = EncodingOptions.FirstOrDefault(option =>
            option.DisplayName.Equals(settings.EncodingName, StringComparison.OrdinalIgnoreCase)) ?? EncodingOptions[0];
        SelectedReceiveDelimiter = ParseEnum(settings.RxDelimiter, ReceiveDelimiterMode.Auto);
        SelectedTransmitDelimiter = _transmitDelimiters.ContainsKey(settings.TxDelimiter) ? settings.TxDelimiter : "CRLF";
        ShowDelimiterTags = settings.ShowDelimiterTags;
        TcpHost = settings.TcpHost;
        TcpPortText = settings.TcpPort.ToString();
        Mode = ParseEnum(settings.Mode, CommunicationMode.Serial);
    }

    private AppSettings CreateSettings() => new()
    {
        SerialParity = SelectedParity.ToString(),
        SerialDataBits = SelectedDataBits,
        SerialStopBits = SelectedStopBits.ToString(),
        SerialHandshake = SelectedHandshake.ToString(),
        SerialDtrEnable = DtrEnable,
        SerialRtsEnable = RtsEnable,
        EncodingName = SelectedEncoding.DisplayName,
        RxDelimiter = SelectedReceiveDelimiter.ToString(),
        TxDelimiter = SelectedTransmitDelimiter,
        ShowDelimiterTags = ShowDelimiterTags,
        TcpHost = TcpHost,
        TcpPort = int.TryParse(TcpPortText, out var port) ? port : 23,
        Mode = Mode.ToString()
    };

    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;

    private static bool UsesRtsFlowControl(Handshake handshake) =>
        handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetimeCts.Cancel();
        _autoFrameFlushTimer.Stop();
        _logRenderTimer.Stop();
        _searchDebounceTimer.Stop();
        _settingsService.Save(CreateSettings());
        _tcpService.Dispose();
        _serialService.Dispose();
        _lifetimeCts.Dispose();
    }
}
