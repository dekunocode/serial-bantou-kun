using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Serial_TCP_IP_Monitor_Avalonia.Services;

public interface IFileDialogService
{
    Task<bool> SaveTextAsync(string suggestedFileName, IEnumerable<string> lines);
}

public sealed class FileDialogService(Func<TopLevel?> topLevelProvider) : IFileDialogService
{
    private static readonly FilePickerFileType TextFileType = new("Text file")
    {
        Patterns = ["*.txt"],
        MimeTypes = ["text/plain"]
    };

    public async Task<bool> SaveTextAsync(string suggestedFileName, IEnumerable<string> lines)
    {
        var topLevel = topLevelProvider();
        if (topLevel?.StorageProvider is not { CanSave: true } storageProvider)
        {
            return false;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "ログを保存",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "txt",
            FileTypeChoices = [TextFileType]
        });

        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        foreach (var line in lines)
        {
            await writer.WriteLineAsync(line);
        }

        return true;
    }
}
