using Microsoft.Extensions.Logging;
using Moq;
using VideoDownloader.Core.Services;
using Xunit;

namespace VideoDownloader.Tests.Services;

public class M3U8ParserTests
{
    private readonly Mock<ILogger<M3U8Parser>> _loggerMock;
    private readonly M3U8Parser _parser;

    public M3U8ParserTests()
    {
        _loggerMock = new Mock<ILogger<M3U8Parser>>();
        _parser = new M3U8Parser(_loggerMock.Object);
    }

    [Fact]
    public void Parse_MasterPlaylist_ReturnsCorrectQualityVariants()
    {
        // Arrange
        var content = @"#EXTM3U
#EXT-X-STREAM-INF:BANDWIDTH=1280000,RESOLUTION=1280x720,CODECS=""avc1.64001f,mp4a.40.2""
http://example.com/720p.m3u8
#EXT-X-STREAM-INF:BANDWIDTH=2560000,RESOLUTION=1920x1080,CODECS=""avc1.640028,mp4a.40.2""
http://example.com/1080p.m3u8";

        // Act
        var playlist = _parser.Parse(content, "http://example.com/master.m3u8");

        // Assert
        Assert.True(playlist.IsMasterPlaylist);
        Assert.Equal(2, playlist.Qualities.Count);
        
        var quality720p = playlist.Qualities[0];
        Assert.Equal(1280000, quality720p.Bandwidth);
        Assert.Equal("1280x720", quality720p.Resolution);
        Assert.Equal("avc1.64001f,mp4a.40.2", quality720p.Codecs);
        Assert.Equal("http://example.com/720p.m3u8", quality720p.Url);

        var quality1080p = playlist.Qualities[1];
        Assert.Equal(2560000, quality1080p.Bandwidth);
        Assert.Equal("1920x1080", quality1080p.Resolution);
        Assert.Equal("avc1.640028,mp4a.40.2", quality1080p.Codecs);
        Assert.Equal("http://example.com/1080p.m3u8", quality1080p.Url);
    }

    [Fact]
    public void Parse_MediaPlaylist_ReturnsCorrectSegments()
    {
        // Arrange
        var content = @"#EXTM3U
#EXT-X-VERSION:3
#EXT-X-TARGETDURATION:10
#EXT-X-MEDIA-SEQUENCE:0
#EXTINF:9.009,
http://example.com/segment1.ts
#EXTINF:9.009,
http://example.com/segment2.ts
#EXTINF:9.009,
http://example.com/segment3.ts
#EXT-X-ENDLIST";

        // Act
        var playlist = _parser.Parse(content, "http://example.com/playlist.m3u8");

        // Assert
        Assert.False(playlist.IsMasterPlaylist);
        Assert.False(playlist.IsLiveStream);
        Assert.Equal(10, playlist.TargetDuration);
        Assert.Equal(3, playlist.Segments.Count);

        Assert.Equal("http://example.com/segment1.ts", playlist.Segments[0].Url);
        Assert.Equal(9.009, playlist.Segments[0].Duration);
        Assert.Equal(0, playlist.Segments[0].SequenceNumber);
    }

    [Fact]
    public void Parse_EncryptedMediaPlaylist_HandlesEncryptionKeys()
    {
        // Arrange
        var content = @"#EXTM3U
#EXT-X-VERSION:3
#EXT-X-KEY:METHOD=AES-128,URI=""key.php"",IV=0x1234567890abcdef1234567890abcdef
#EXTINF:9.009,
segment1.ts
#EXTINF:9.009,
segment2.ts";

        // Act
        var playlist = _parser.Parse(content, "http://example.com/playlist.m3u8");

        // Assert
        Assert.Single(playlist.EncryptionKeys);
        Assert.Equal("http://example.com/key.php", playlist.Segments[0].EncryptionKeyUrl);
        Assert.Equal("1234567890abcdef1234567890abcdef", playlist.Segments[0].EncryptionIV);
    }

    [Fact]
    public void Parse_LiveStream_DetectsLiveCorrectly()
    {
        // Arrange
        var content = @"#EXTM3U
#EXT-X-VERSION:3
#EXT-X-TARGETDURATION:10
#EXT-X-MEDIA-SEQUENCE:1234
#EXTINF:9.009,
segment1234.ts
#EXTINF:9.009,
segment1235.ts";  // No #EXT-X-ENDLIST indicates live stream

        // Act
        var playlist = _parser.Parse(content, "http://example.com/live.m3u8");

        // Assert
        Assert.True(playlist.IsLiveStream);
        Assert.Equal(1234, playlist.Segments[0].SequenceNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#INVALID")]
    [InlineData("#EXTM3")]
    public void Parse_InvalidPlaylist_ThrowsFormatException(string content)
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => _parser.Parse(content, "http://example.com/invalid.m3u8"));
    }
}
