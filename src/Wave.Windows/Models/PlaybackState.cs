using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wave.Windows.Models;

/// <summary>
/// Estado observable del playback. La UI bindea a esta clase
/// a través de PlaybackController (que hereda de ObservableObject).
/// </summary>
public partial class PlaybackState : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Song> _playlist = new();
    [ObservableProperty] private int _currentWindow = -1;
    [ObservableProperty] private long _positionMs;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private bool _repeatOne;
    [ObservableProperty] private bool _hasNext;

    public Song? CurrentSong => Playlist.Count > 0 && CurrentWindow >= 0 && CurrentWindow < Playlist.Count
        ? Playlist[CurrentWindow]
        : null;
}
