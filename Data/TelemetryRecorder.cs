using System;
using System.IO;
using System.Text;
using cAlgo.API;

public class TelemetryRecorder
{
    private readonly string _symbol;
    private readonly string _filePath;

    public TelemetryRecorder(string symbol)
    {
        _symbol = symbol;

        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GeminiTelemetry");

        Directory.CreateDirectory(folder);

        string fileName = $"{symbol}_M5_{DateTime.UtcNow:yyyyMMdd}.jsonl";
        _filePath = Path.Combine(folder, fileName);
    }

    public void Write(string jsonLine)
    {
        File.AppendAllText(_filePath, jsonLine + Environment.NewLine, Encoding.UTF8);
    }
}
