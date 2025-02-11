﻿using System.CommandLine;
using ImgGoCli.BlobStores;
using ImgGoCli.Configs;
using ImgGoCli.Utils;

namespace ImgGoCli.CommandLine.Commands;

public class ImgCommand : Command
{
    public ImgCommand() : base("img", "处理单个图片文件或图片目录")
    {
        var storeOption = CommandOptions.StoreOption();
        var configFileOption = CommandOptions.AppConfigPathOption();
        var outputPathOption = CommandOptions.OutPutPathOption();
        var watermarkOption = CommandOptions.WatermarkOption();
        var compressLevelOption = CommandOptions.CompressLevelOption();
        var skipFileWhenExceptionOption = CommandOptions.SkipFileWhenException();
        var filePathArgument = CommandArguments.FileSystemInfoArgument();
        AddOption(storeOption);
        AddOption(configFileOption);
        AddOption(outputPathOption);
        AddOption(watermarkOption);
        AddOption(compressLevelOption);
        AddArgument(filePathArgument);
        this.SetHandler(CommandHandler, storeOption, configFileOption, filePathArgument, outputPathOption,
            watermarkOption, compressLevelOption, skipFileWhenExceptionOption);
    }

    internal static async Task CommandHandler(BlobStoresEnum? store,
        FileInfo configsFile,
        FileSystemInfo imageDirectoryOrFile,
        string? outputPath,
        bool? addWatermark,
        bool? compressionImg,
        bool? skipFileWhenException)
    {
        var config = await AppConfigs.LoadConfigsAsync(configsFile);
        config.DefaultBlobStore = store?.ToString() ?? config.DefaultBlobStore;
        config.DefaultOutputPath = outputPath ?? config.DefaultOutputPath;
        config.AddWatermark = addWatermark ?? config.AddWatermark;
        config.CompressionImage = compressionImg ?? config.CompressionImage;
        config.SkipFileWhenException = skipFileWhenException ?? config.SkipFileWhenException;
        config.BasicConfigValidation();

        LogUtil.Notify(config.ToString());

        var blobStoresAccessor = new BlobStoresAccessor(config);

        switch (imageDirectoryOrFile)
        {
            case FileInfo file:
                await ImageFileHandler(file, config, blobStoresAccessor);
                break;
            case DirectoryInfo directoryInfo:
            {
                foreach (var fileInfo in directoryInfo.GetFiles())
                {
                    try
                    {
                        await ImageFileHandler(fileInfo, config, blobStoresAccessor);
                    }
                    catch (Exception ex) when (config.SkipFileWhenException)
                    {
                        LogUtil.Error($"跳过文件[{fileInfo.Name}]，异常原因：处理失败-{ex.Message}");
                    }
                }

                break;
            }
        }
    }

    internal static async Task<string> ImageFileHandler(
        FileInfo imageFile,
        AppConfigs config,
        BlobStoresAccessor blobStoresAccessor)
    {
        ProcessImageResult? result = null;
        try
        {
            LogUtil.Info($"\n图片开始处理：[{Path.GetFileName(imageFile.FullName)}]");
            var (skip, imgStream, fileName) = result = ImgUtil.ProcessImage(
                config.AddWatermark,
                config.CompressionImage,
                config.ImageConfigs,
                imageFile);
            var length = imgStream.Length;

            if (skip)
            {
                LogUtil.Error("由于文件类型不支持水印、压缩处理，已跳过");
            }
            else
            {
                Console.WriteLine("图片水印、压缩、转换处理成功");
            }

            var accessUrl = await blobStoresAccessor.DoStoreAsync(config.DefaultBlobStore!, imgStream, fileName);

            var displayUrl = accessUrl;
            if (displayUrl.Length > 256)
            {
                displayUrl = $"路径太长，已省略中间部分 - {displayUrl.AsSpan()[..128]} ... {displayUrl.AsSpan()[^128..]}";
            }

            LogUtil.Info($"图片存储成功，存储路径：{displayUrl}");
            LogUtil.Info($"大小：{imageFile.Length / 1024.0:F}Kb -> {length / 1024.0:F}Kb");
            return accessUrl;
        }
        finally
        {
            if (result?.Stream is not null)
            {
                await result.Stream.DisposeAsync();
            }
        }
    }
}