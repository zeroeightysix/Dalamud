﻿using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;

using Dalamud.Storage;

using Xunit;

namespace Dalamud.Test.Storage;

public class ReliableFileStorageTests
{
    private const string DbFileName = "dalamudVfs.db";
    private const string TestFileName = "file.txt";
    private const string TestFileContent1 = "hello from señor dalamundo";
    private const string TestFileContent2 = "rewritten";

    [Fact]
    public async Task IsConcurrencySafe()
    {
        var dbDir = CreateTempDir();
        var rfs = new DisposableReliableFileStorage(dbDir);
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);

        // Do reads/writes/deletes on the same file on many threads at once and
        // see if anything throws
        await Task.WhenAll(
            Enumerable.Range(1, 6)
                      .Select(
                          i => Parallel.ForEachAsync(
                              Enumerable.Range(1, 100),
                              (j, _) =>
                              {
                                  if (i % 2 == 0)
                                  {
                                      // ReSharper disable once AccessToDisposedClosure
                                      rfs.Instance.WriteAllText(tempFile, j.ToString());
                                  }
                                  else if (i % 3 == 0)
                                  {
                                      try
                                      {
                                          // ReSharper disable once AccessToDisposedClosure
                                          rfs.Instance.ReadAllText(tempFile);
                                      }
                                      catch (FileNotFoundException)
                                      {
                                          // this is fine
                                      }
                                  }
                                  else
                                  {
                                      File.Delete(tempFile);
                                  }

                                  return ValueTask.CompletedTask;
                              })));
    }

    [Fact]
    public void Constructor_Dispose_Works()
    {
        var dbDir = CreateTempDir();
        var dbPath = Path.Combine(dbDir, DbFileName);
        using var rfs = new DisposableReliableFileStorage(dbDir);

        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void Exists_ThrowsIfPathIsEmpty()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentException>(() => rfs.Instance.Exists(""));
    }

    [Fact]
    public void Exists_ThrowsIfPathIsNull()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentNullException>(() => rfs.Instance.Exists(null!));
    }

    [Fact]
    public void Exists_WhenFileMissing_ReturnsFalse()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        Assert.False(rfs.Instance.Exists(tempFile));
    }

    [Fact]
    public void Exists_WhenFileMissing_WhenDbFailed_ReturnsFalse()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateFailedRfs();

        Assert.False(rfs.Instance.Exists(tempFile));
    }

    [Fact]
    public async Task Exists_WhenFileOnDisk_ReturnsTrue()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        await File.WriteAllTextAsync(tempFile, TestFileContent1);
        using var rfs = CreateRfs();

        Assert.True(rfs.Instance.Exists(tempFile));
    }

    [Fact]
    public void Exists_WhenFileInBackup_ReturnsTrue()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);

        File.Delete(tempFile);
        Assert.True(rfs.Instance.Exists(tempFile));
    }

    [Fact]
    public void Exists_WhenFileInBackup_WithDifferentContainerId_ReturnsFalse()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);

        File.Delete(tempFile);
        Assert.False(rfs.Instance.Exists(tempFile, Guid.NewGuid()));
    }

    [Fact]
    public void WriteAllText_ThrowsIfPathIsEmpty()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentException>(() => rfs.Instance.WriteAllText("", TestFileContent1));
    }

    [Fact]
    public void WriteAllText_ThrowsIfPathIsNull()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentNullException>(() => rfs.Instance.WriteAllText(null!, TestFileContent1));
    }

    [Fact]
    public async Task WriteAllText_WritesToDbAndDisk()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);

        Assert.True(File.Exists(tempFile));
        Assert.Equal(TestFileContent1, rfs.Instance.ReadAllText(tempFile, forceBackup: true));
        Assert.Equal(TestFileContent1, await File.ReadAllTextAsync(tempFile));
    }

    [Fact]
    public void WriteAllText_SeparatesContainers()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        var containerId = Guid.NewGuid();

        using var rfs = CreateRfs();
        rfs.Instance.WriteAllText(tempFile, TestFileContent1);
        rfs.Instance.WriteAllText(tempFile, TestFileContent2, containerId);
        File.Delete(tempFile);

        Assert.Equal(TestFileContent1, rfs.Instance.ReadAllText(tempFile, forceBackup: true));
        Assert.Equal(TestFileContent2, rfs.Instance.ReadAllText(tempFile, forceBackup: true, containerId));
    }

    [Fact]
    public async Task WriteAllText_WhenDbFailed_WritesToDisk()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateFailedRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);

        Assert.True(File.Exists(tempFile));
        Assert.Equal(TestFileContent1, await File.ReadAllTextAsync(tempFile));
    }

    [Fact]
    public async Task WriteAllText_CanUpdateExistingFile()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);
        rfs.Instance.WriteAllText(tempFile, TestFileContent2);

        Assert.True(File.Exists(tempFile));
        Assert.Equal(TestFileContent2, rfs.Instance.ReadAllText(tempFile, forceBackup: true));
        Assert.Equal(TestFileContent2, await File.ReadAllTextAsync(tempFile));
    }

    [Fact]
    public void WriteAllText_SupportsNullContent()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, null);

        Assert.True(File.Exists(tempFile));
        Assert.Equal("", rfs.Instance.ReadAllText(tempFile));
    }

    [Fact]
    public void ReadAllText_ThrowsIfPathIsEmpty()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentException>(() => rfs.Instance.ReadAllText(""));
    }

    [Fact]
    public void ReadAllText_ThrowsIfPathIsNull()
    {
        using var rfs = CreateRfs();
        Assert.Throws<ArgumentNullException>(() => rfs.Instance.ReadAllText(null!));
    }

    [Fact]
    public async Task ReadAllText_WhenFileOnDisk_ReturnsContent()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        await File.WriteAllTextAsync(tempFile, TestFileContent1);
        using var rfs = CreateRfs();

        Assert.Equal(TestFileContent1, rfs.Instance.ReadAllText(tempFile));
    }

    [Fact]
    public void ReadAllText_WhenFileMissingWithBackup_ReturnsContent()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);
        File.Delete(tempFile);

        Assert.Equal(TestFileContent1, rfs.Instance.ReadAllText(tempFile));
    }

    [Fact]
    public void ReadAllText_WhenFileMissingWithBackup_ThrowsWithDifferentContainerId()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        var containerId = Guid.NewGuid();
        using var rfs = CreateRfs();

        rfs.Instance.WriteAllText(tempFile, TestFileContent1);
        File.Delete(tempFile);

        Assert.Throws<FileNotFoundException>(() => rfs.Instance.ReadAllText(tempFile, containerId: containerId));
    }

    [Fact]
    public void ReadAllText_WhenFileMissing_ThrowsIfDbFailed()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateFailedRfs();
        Assert.Throws<FileNotFoundException>(() => rfs.Instance.ReadAllText(tempFile));
    }

    [Fact]
    public async Task ReadAllText_WithReader_WhenFileOnDisk_ReadsContent()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        await File.WriteAllTextAsync(tempFile, TestFileContent1);
        using var rfs = CreateRfs();
        rfs.Instance.ReadAllText(tempFile, text => Assert.Equal(TestFileContent1, text));
    }

    [Fact]
    public async Task ReadAllText_WithReader_WhenReaderThrows_ThrowsIfBackupMissing()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        await File.WriteAllTextAsync(tempFile, TestFileContent1);

        var readerCalledOnce = false;

        using var rfs = CreateRfs();
        Assert.Throws<FileReadException>(() => rfs.Instance.ReadAllText(tempFile, Reader));

        return;

        void Reader(string text)
        {
            var wasReaderCalledOnce = readerCalledOnce;
            readerCalledOnce = true;
            if (!wasReaderCalledOnce) throw new Exception();
        }
    }

    [Fact]
    public void ReadAllText_WithReader_WhenReaderThrows_ReadsContentFromBackup()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);

        var readerCalledOnce = false;
        var assertionCalled = false;

        using var rfs = CreateRfs();
        rfs.Instance.WriteAllText(tempFile, TestFileContent1);
        File.Delete(tempFile);

        rfs.Instance.ReadAllText(tempFile, Reader);
        Assert.True(assertionCalled);

        return;

        void Reader(string text)
        {
            var wasReaderCalledOnce = readerCalledOnce;
            readerCalledOnce = true;
            if (!wasReaderCalledOnce) throw new Exception();
            Assert.Equal(TestFileContent1, text);
            assertionCalled = true;
        }
    }

    [Fact]
    public async Task ReadAllText_WithReader_RethrowsFileNotFoundException()
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        await File.WriteAllTextAsync(tempFile, TestFileContent1);
        using var rfs = CreateRfs();
        Assert.Throws<FileNotFoundException>(() => rfs.Instance.ReadAllText(tempFile, _ => throw new FileNotFoundException()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadAllText_WhenFileDoesNotExist_Throws(bool forceBackup)
    {
        var tempFile = Path.Combine(CreateTempDir(), TestFileName);
        using var rfs = CreateRfs();
        Assert.Throws<FileNotFoundException>(() => rfs.Instance.ReadAllText(tempFile, forceBackup));
    }

    private static DisposableReliableFileStorage CreateRfs()
    {
        var dbDir = CreateTempDir();
        return new(dbDir);
    }

    private static DisposableReliableFileStorage CreateFailedRfs()
    {
        var dbDir = CreateTempDir();
        var dbPath = Path.Combine(dbDir, DbFileName);

        // Create a corrupt DB deliberately, and hold its handle until
        // the end of the scope
        using var f = File.Open(dbPath, FileMode.CreateNew);
        f.Write("broken"u8);

        // Throws an SQLiteException initially, and then throws an
        // IOException when attempting to delete the file because
        // there's already an active handle associated with it
        return new(dbDir);
    }

    private static string CreateTempDir()
    {
        string tempDir;
        do
        {
            // Generate temp directories until we get a new one (usually happens on the first try)
            tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        }
        while (File.Exists(tempDir));

        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private sealed class DisposableReliableFileStorage : IDisposable
    {
        public DisposableReliableFileStorage(string rfsDbPath) => this.Instance = new(rfsDbPath);

        public ReliableFileStorage Instance { get; }

        public void Dispose() => ((IInternalDisposableService)this.Instance).DisposeService();
    }
}
