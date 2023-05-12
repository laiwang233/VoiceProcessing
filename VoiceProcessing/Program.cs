using NAudio.Wave;

public class ConsoleApp
{
    private static string OldPath { get; set; }
    private static string NewPath { get; set; }

    public static async Task Main(string[] args)
    {
        Console.WriteLine("请输入你的音频文件夹路径");
        OldPath = Console.ReadLine() ?? string.Empty;
        Console.WriteLine("请输入处理完成后的文件夹路径");
        NewPath = Console.ReadLine() ?? string.Empty;

        try
        {
            Path.GetFullPath(OldPath);
            Path.GetFullPath(NewPath);

            if (!string.IsNullOrEmpty(OldPath) && !string.IsNullOrEmpty(NewPath))
            {
                //筛选wav
                FilterWav(OldPath, NewPath);

                //将过短的wav拼接
                ConcatenateWavFiles("ConcatenateWavFiles");

                //将过长的wav切片
                SliceMultipleWavFiles("SectionWavFiles", 5);

                Console.WriteLine("处理完成");
                Console.WriteLine("按任意键退出");
                Console.ReadKey();
            }
            else
            {
                Console.WriteLine("文件夹路径不可为空");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("文件夹路径异常");
        }
    }

    /// <summary>
    /// 筛选wav
    /// </summary>
    /// <param name="path"></param>
    /// <param name="newPath"></param>
    private static void FilterWav(string path, string newPath)
    {
        var okPath = Path.Combine(newPath, "OK");
        var ngPath = Path.Combine(newPath, "NG");
        var ngLongPath = Path.Combine(ngPath, "long");
        var ngShortPath = Path.Combine(ngPath, "short");

        //如果文件夹不存在则创建
        if (!Path.Exists(okPath)) Directory.CreateDirectory(okPath);
        if (!Path.Exists(ngPath))
        {
            Directory.CreateDirectory(ngPath);
            Directory.CreateDirectory(ngLongPath);
            Directory.CreateDirectory(ngShortPath);
        }

        var fileNames = new DirectoryInfo(path)
            .GetFiles()
            .Select(s => s.Name)
            .Where(s => Path.GetExtension(s) == ".wav")
            .ToList();

        foreach (var fileName in fileNames)
        {
            var oldFileName = Path.Combine(path, fileName);
            var newFileNameTrue = Path.Combine(okPath, fileName);

            var size = GetWavFileDuration(oldFileName);

            switch (size.Seconds)
            {
                //不符合要求的文件会复制到NG文件夹
                case < 2:
                    File.Copy(oldFileName, Path.Combine(ngShortPath, fileName));
                    continue;
                case > 20:
                    File.Copy(oldFileName, Path.Combine(ngLongPath, fileName));
                    continue;
                default:
                    //符合要求的文件会复制到OK文件夹
                    File.Copy(oldFileName, newFileNameTrue);
                    break;
            }
        }
    }

    private static TimeSpan GetWavFileDuration(string filePath)
    {
        using var waveFileReader = new WaveFileReader(filePath);
        return waveFileReader.TotalTime;
    }

    /// <summary>
    /// 将过短的音频拼接成不超过15秒的音频
    /// </summary>
    /// <param name="outputFilePrefix">拼接后的文件名前缀</param>
    /// <exception cref="InvalidOperationException"></exception>
    private static void ConcatenateWavFiles(string outputFilePrefix)
    {
        double totalDuration = 0;
        var outputFileIndex = 1;
        WaveFileWriter waveFileWriter = null;

        var ngShortPath = Path.Combine(NewPath, "NG", "short");
        var okPath = Path.Combine(NewPath, "OK");

        var fileNames = new DirectoryInfo(ngShortPath)
            .GetFiles()
            .Select(s => s.Name)
            .Where(s => Path.GetExtension(s) == ".wav")
            .ToList();

        foreach (var inputFile in fileNames.Select(fileName => Path.Combine(ngShortPath, fileName)))
        {
            using var waveFileReader = new WaveFileReader(inputFile);
            if (waveFileWriter == null)
            {
                waveFileWriter = new WaveFileWriter(Path.Combine(okPath, $"{outputFilePrefix}_{outputFileIndex}.wav"), waveFileReader.WaveFormat);
            }
            else if (waveFileWriter.WaveFormat.Encoding != waveFileReader.WaveFormat.Encoding
                     || waveFileWriter.WaveFormat.SampleRate != waveFileReader.WaveFormat.SampleRate
                     || waveFileWriter.WaveFormat.Channels != waveFileReader.WaveFormat.Channels)
            {
                continue;
            }

            var inputDuration = waveFileReader.TotalTime.TotalSeconds;
            totalDuration += inputDuration;

            if (totalDuration > 15)
            {
                waveFileWriter.Dispose();
                totalDuration = inputDuration;
                outputFileIndex++;
                waveFileWriter = new WaveFileWriter(Path.Combine(okPath, $"{outputFilePrefix}_{outputFileIndex}.wav"), waveFileReader.WaveFormat);
            }

            var buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = waveFileReader.Read(buffer, 0, buffer.Length)) > 0)
            {
                waveFileWriter.Write(buffer, 0, bytesRead);
            }
        }

        waveFileWriter?.Dispose();
    }

    /// <summary>
    /// 把超过15秒的音频文件以每个文件5秒切片
    /// </summary>
    /// <param name="outputFilePrefix"></param>
    /// <param name="sliceDurationInSeconds"></param>
    private static void SliceMultipleWavFiles(string outputFilePrefix, int sliceDurationInSeconds)
    {
        var ngLongPath = Path.Combine(NewPath, "NG", "long");
        var okShortPath = Path.Combine(NewPath, "OK");

        var fileNames = new DirectoryInfo(ngLongPath)
            .GetFiles()
            .Select(s => s.Name)
            .Where(s => Path.GetExtension(s) == ".wav")
            .ToList();

        foreach (var inputFile in fileNames.Select(s => Path.Combine(ngLongPath, s)))
        {
            try
            {
                using var waveFileReader = new WaveFileReader(inputFile);
                var bytesPerMillisecond = waveFileReader.WaveFormat.AverageBytesPerSecond / 1000;
                var sliceByteSize = sliceDurationInSeconds * bytesPerMillisecond * 1000;
                var sliceCount = 1;
                var buffer = new byte[sliceByteSize];

                while (waveFileReader.Position < waveFileReader.Length)
                {
                    var outputFileName = Path.Combine(okShortPath, $"{outputFilePrefix}_{Guid.NewGuid()}_{sliceCount}.wav");
                    var bytesRead = waveFileReader.Read(buffer, 0, sliceByteSize);

                    using (var waveFileWriter = new WaveFileWriter(outputFileName, waveFileReader.WaveFormat))
                    {
                        waveFileWriter.Write(buffer, 0, bytesRead);
                    }

                    sliceCount++;
                }
            }
            catch (FormatException e)
            {
                Console.WriteLine("音频格式错误");
                throw;
            }
        }
    }
}