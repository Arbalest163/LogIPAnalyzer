using LogIPAnalyzer.Constants;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;

const char PartSeparator = ':';
const string DateFormatParameter = "dd.MM.yyyy";
const string DateFormatJournal = "yyyy-MM-dd HH:mm:ss";

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

string fileLogPath = configuration[ParameterNames.FileLog];
string fileOutputPath = configuration[ParameterNames.FileOutput];
string addressStart = configuration[ParameterNames.AddressStart];
string addressMaskStr = configuration[ParameterNames.AddressMask];
string timeStartStr = configuration[ParameterNames.TimeStart];
string timeEndStr = configuration[ParameterNames.TimeEnd];

try
{
    if (string.IsNullOrEmpty(fileLogPath))
    {
        throw new Exception("Не указан путь файла логов");
    }
    if (string.IsNullOrEmpty(fileOutputPath))
    {
        throw new Exception("Не указан выходной путь");
    }
    if (!DateTime.TryParseExact(timeStartStr, DateFormatParameter, null, DateTimeStyles.None, out var timeStart))
    {
        throw new Exception($"Некорректное значение нижней границы временного интервала: {timeStartStr}. Допустимый формат даты: {DateFormatParameter}");
    }
    if (!DateTime.TryParseExact(timeEndStr, DateFormatParameter, null, DateTimeStyles.None, out var timeEnd))
    {
        throw new Exception($"Некорректное значение верхней границы временного интервала: {timeEndStr}. Допустимый формат даты: {DateFormatParameter}");
    }

    Int32.TryParse(addressMaskStr, out var addressMask);

    ProcessLogFile(fileLogPath, fileOutputPath, timeStart, timeEnd, addressStart, addressMask);
    Console.WriteLine("Обработка файла логов завершена.");
}
catch (Exception ex)
{
    Console.WriteLine($"Ошибка при выполнении программы: {ex.Message}");
}

static void ProcessLogFile(string fileLogPath, string fileOutputPath, DateTime timeStart, DateTime timeEnd, string startAddressRaw = "", int mask = 0)
{
    var addressCountsDict = new Dictionary<string, int>();
    if(!IPAddress.TryParse(startAddressRaw, out var startAddress))
    {
        if (!string.IsNullOrEmpty(startAddressRaw))
        {
            Console.WriteLine($"Некорректное значение нижней границы адресов: {startAddressRaw}. Будут обработаны все адреса.");
        }
    }
    var isNoMask = startAddress == null || mask == 0;

    ReadFile();
    WriteFile();

    void ReadFile()
    {
        using var reader = new StreamReader(fileLogPath);
        string? line = string.Empty;

        while ((line = reader.ReadLine()) != null)
        {
            var splitIndex = line.IndexOf(PartSeparator);
            if (splitIndex == -1) 
            { 
                continue;
            }

            var ipAddressRaw = line.Substring(0, splitIndex);
            var timestampStr = line.Substring(splitIndex + 1);

            if(!IPAddress.TryParse(ipAddressRaw, out var ipAddress))
            {
                continue;
            }

            if (!DateTime.TryParseExact(timestampStr, DateFormatJournal, null, DateTimeStyles.None, out var accessTime))
            {
                continue;
            }

            var availableIpAddress = (isNoMask || IsInRange(ipAddress, startAddress!, mask)) && IsInTimeRange(accessTime, timeStart, timeEnd);
            if (!availableIpAddress)
            {
                continue;
            }

            if (!addressCountsDict.ContainsKey(ipAddressRaw))
            {
                addressCountsDict[ipAddressRaw] = 0;
            }
            addressCountsDict[ipAddressRaw]++;
        }
    }

    void WriteFile()
    {
        using StreamWriter writer = new StreamWriter(fileOutputPath);

        foreach (var entry in addressCountsDict)
        {
            writer.WriteLine($"{entry.Key}: {entry.Value}");
        }
    }
}

static bool IsInRange(IPAddress ipAddress, IPAddress startAddress, int mask)
{
    if(mask == 0)
    {
        return true;
    }
    var addressBytes = ipAddress.GetAddressBytes();
    var startBytes = startAddress.GetAddressBytes();

    var byteCount = mask / 8;
    var bitCount = mask % 8;

    for (int i = 0; i < byteCount; i++)
    {
        if (addressBytes[i] != startBytes[i])
        { 
            return false;
        }
    }

    if (bitCount != 0)
    {
        var maskByte = (byte)~(255 >> bitCount);
        if ((addressBytes[byteCount] & maskByte) != (startBytes[byteCount] & maskByte))
        {
            return false;
        }
    }

    return true;
}

static bool IsInTimeRange(DateTime accessTime, DateTime start, DateTime end)
{
    return accessTime >= start && accessTime <= end;
}
