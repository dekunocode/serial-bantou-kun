# シリアル番頭くん

シリアル（COM）・TCP通信を、ひとつの画面で見張るWindows用の通信モニターです。
マイコンボードのデバッグや、LAN対応機器とのやり取りを、帳場の番頭さんのように送受信をさばいて記録します。

Avalonia 12 + .NET 10 で作った、インストール不要の単一exeです。

## 主な機能

- **2つの接続方式**：シリアル（COM）／TCPクライアント
- **シリアル設定**：ボーレート、パリティ、データ長、ストップビット、フロー制御、DTR / RTS
- **COMポートのデバイス名表示**：「USB Serial Port (COM3)」のように、どの機器かひと目でわかります
- **文字コード**：UTF-8 / ASCII / Shift-JIS
- **Text / Hex 送受信**
- **受信区切り**：None / Auto / CR / LF / CRLF
- **制御コード送信**：`[STX]`、`[ETX]`、`[0xFF]` のように角かっこで書けます
- **ログ**：RX / TX / SYSTEM / ERROR の色分け、検索、種別フィルター、自動スクロール、ファイル保存
- **送信まわり**：Enterで送信、↑↓キーで送信履歴
- **RX / TX バイトカウンター**（接続ごと）
- **設定の自動保存**（JSON）

## ダウンロードと起動

[Releases](../../releases) から `SerialBantouKun.exe` をダウンロードして、そのまま起動してください。
.NETランタイムを含んだ自己完結の単一exeなので、インストールは不要です。

- 対応OS：Windows 10 / 11（x64）
- 設定ファイルの保存先：`%APPDATA%\SerialBantouKun\settings.json`

## ソースからビルド

.NET 10 SDK が必要です。

```powershell
dotnet build Serial_TCP-IP_Monitor_Avalonia.slnx --configuration Debug
```

テスト：

```powershell
dotnet run --project Serial_TCP-IP_Monitor_Avalonia.Tests\Serial_TCP-IP_Monitor_Avalonia.Tests.csproj --configuration Debug
```

### 単一exeの発行

```powershell
dotnet publish Serial_TCP-IP_Monitor_Avalonia\Serial_TCP-IP_Monitor_Avalonia.csproj `
  --configuration Release `
  --runtime win-x64 `
  --output artifacts\release\win-x64
```

`artifacts\release\win-x64\SerialBantouKun.exe` の1ファイルができます。

配布版は、起動を速くするため ReadyToRun（事前コンパイル）＋単一ファイル圧縮で発行しています。
同じPCで発行方法を変えて比べた結果は次のとおりです（ウィンドウが表示されるまでの時間、2回目以降の平均）。

| 発行方法 | exeサイズ | 起動時間 |
|---|---:|---:|
| 通常・圧縮あり | 48 MiB | 約1,080 ms |
| 通常・圧縮なし | 100 MiB | 約990 ms |
| **ReadyToRun・圧縮あり（配布版）** | **63 MiB** | **約530 ms** |
| ReadyToRun・圧縮なし | 141 MiB | 約370 ms |

Native AOT 版のプロファイル（`WinX64NativeAot`）も入れてありますが、発行には Visual Studio の「C++によるデスクトップ開発」ワークロードが必要です。

## ライセンス

[MIT License](LICENSE)

