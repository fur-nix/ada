using Ada.Core;
using Windows.Media.Control;
using GSMTC = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Ada.Services;

/// <summary>Windows medya oturumu (Spotify, tarayıcı, Groove...) — olaylar UI iş parçacığına taşınır.</summary>
public sealed class MediaService
{
    GSMTC? _mgr;
    Session? _session;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Source { get; private set; } = "";
    public bool Playing { get; private set; }
    public bool HasSession => _session != null && Title.Length > 0;
    public byte[]? Art { get; private set; }
    public int ArtVersion { get; private set; }
    public TimeSpan Position, Duration;
    public DateTime PositionStamp;

    /// <summary>Parça değiştiğinde (UI iş parçacığında) tetiklenir.</summary>
    public event Action? TrackChanged;
    public event Action? Changed;

    public async void Start()
    {
        try
        {
            _mgr = await GSMTC.RequestAsync();
            _mgr.CurrentSessionChanged += (_, _) => Dispatcher.Post(Attach);
            Dispatcher.Post(Attach);
        }
        catch (Exception e) { Log.Error("Media.Start", e); }
    }

    void Attach()
    {
        var s = _mgr?.GetCurrentSession();
        if (ReferenceEquals(s, _session)) return;
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = s;
        if (s == null)
        {
            Title = Artist = Source = ""; Playing = false; Art = null; ArtVersion++;
            Changed?.Invoke();
            return;
        }
        s.MediaPropertiesChanged += OnProps;
        s.PlaybackInfoChanged += OnPlayback;
        s.TimelinePropertiesChanged += OnTimeline;
        Source = PrettySource(s.SourceAppUserModelId);
        RefreshPlayback(); RefreshTimeline(); RefreshProps();
    }

    void OnProps(Session s, MediaPropertiesChangedEventArgs e) => Dispatcher.Post(RefreshProps);
    void OnPlayback(Session s, PlaybackInfoChangedEventArgs e) => Dispatcher.Post(RefreshPlayback);
    void OnTimeline(Session s, TimelinePropertiesChangedEventArgs e) => Dispatcher.Post(RefreshTimeline);

    async void RefreshProps()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            byte[]? art = null;
            if (p.Thumbnail != null)
            {
                using var stream = await p.Thumbnail.OpenReadAsync();
                using var net = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await net.CopyToAsync(ms);
                art = ms.ToArray();
            }
            Dispatcher.Post(() =>
            {
                if (!ReferenceEquals(s, _session)) return;
                bool newTrack = p.Title != Title || p.Artist != Artist;
                Title = p.Title ?? ""; Artist = p.Artist ?? "";
                if (string.IsNullOrEmpty(Artist)) Artist = p.AlbumArtist ?? "";
                // aynı kapak tekrar gelirse yeniden çözme / çapraz geçiş yapma
                bool artChanged = art == null ? Art != null : Art == null || !art.AsSpan().SequenceEqual(Art);
                if (artChanged) { Art = art; ArtVersion++; }
                Changed?.Invoke();
                if (newTrack && Title.Length > 0) TrackChanged?.Invoke();
            });
        }
        catch (Exception e) { Log.Error("Media.Props", e); }
    }

    void RefreshPlayback()
    {
        try
        {
            var info = _session?.GetPlaybackInfo();
            Playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            RefreshTimeline();
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Error("Media.Playback", e); }
    }

    void RefreshTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return;
            Position = t.Position;
            Duration = t.EndTime - t.StartTime;
            PositionStamp = t.LastUpdatedTime.LocalDateTime;
            if (PositionStamp > DateTime.Now || PositionStamp.Year < 2000) PositionStamp = DateTime.Now;
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Error("Media.Timeline", e); }
    }

    /// <summary>Şu anki tahmini konum (oynatılıyorsa zaman damgasından ilerletilir).</summary>
    public TimeSpan LivePosition
    {
        get
        {
            var p = Position;
            if (Playing) p += DateTime.Now - PositionStamp;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p;
        }
    }

    public async void TogglePlay()
    {
        if (_session == null) return;
        Playing = !Playing; Changed?.Invoke(); // iyimser güncelleme → anında tepki
        try { await _session.TryTogglePlayPauseAsync(); } catch (Exception e) { Log.Error("Media.Toggle", e); }
    }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch (Exception e) { Log.Error("Media.Next", e); } }
    public async void Prev() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch (Exception e) { Log.Error("Media.Prev", e); } }
    public async void Seek(double fraction)
    {
        if (_session == null || Duration <= TimeSpan.Zero) return;
        var target = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        Position = target; PositionStamp = DateTime.Now; Changed?.Invoke();
        try { await _session.TryChangePlaybackPositionAsync(target.Ticks); } catch (Exception e) { Log.Error("Media.Seek", e); }
    }

    static string PrettySource(string aumid)
    {
        var a = aumid.ToLowerInvariant();
        if (a.Contains("spotify")) return "Spotify";
        if (a.Contains("chrome")) return "Chrome";
        if (a.Contains("msedge") || a.Contains("edge")) return "Edge";
        if (a.Contains("firefox")) return "Firefox";
        if (a.Contains("opera")) return "Opera";
        if (a.Contains("brave")) return "Brave";
        if (a.Contains("zunemusic") || a.Contains("media player")) return "Medya Oynatıcı";
        if (a.Contains("vlc")) return "VLC";
        if (a.Contains("youtube")) return "YouTube";
        var n = Path.GetFileNameWithoutExtension(aumid.Split('!')[0]);
        return n.Length > 0 ? char.ToUpper(n[0]) + n[1..] : "Medya";
    }
}
