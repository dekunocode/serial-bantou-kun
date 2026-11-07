using System.Text.Json.Serialization;
using Serial_TCP_IP_Monitor_Avalonia.Models;

namespace Serial_TCP_IP_Monitor_Avalonia.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
public partial class AppJsonContext : JsonSerializerContext;
