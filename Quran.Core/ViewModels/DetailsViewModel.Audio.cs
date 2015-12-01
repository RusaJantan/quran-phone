// --------------------------------------------------------------------------------------------------------------------
// <summary>
//    Defines the DetailsViewModel type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Quran.Core.Common;
using Quran.Core.Data;
using Quran.Core.Properties;
using Quran.Core.Utils;

namespace Quran.Core.ViewModels
{
    /// <summary>
    /// Define the DetailsViewModel type.
    /// </summary>
    public partial class DetailsViewModel : ViewModelWithDownload
    {
        #region Properties
        private AudioState audioPlayerState;
        public AudioState AudioPlayerState
        {
            get { return audioPlayerState; }
            set
            {
                if (value == audioPlayerState)
                    return;

                audioPlayerState = value;
                base.OnPropertyChanged(() => AudioPlayerState);
            }
        }

        private bool isDownloadingAudio;
        public bool IsDownloadingAudio
        {
            get { return isDownloadingAudio; }
            set
            {
                if (value == isDownloadingAudio)
                    return;

                isDownloadingAudio = value;
                base.OnPropertyChanged(() => IsDownloadingAudio);
            }
        }

        private int audioDownloadProgress;
        public int AudioDownloadProgress
        {
            get { return audioDownloadProgress; }
            set
            {
                if (value == audioDownloadProgress)
                    return;

                audioDownloadProgress = value;
                base.OnPropertyChanged(() => AudioDownloadProgress);
            }
        }

        private bool? repeatAudio;
        public bool? RepeatAudio
        {
            get { return repeatAudio; }
            set
            {
                if (value == repeatAudio)
                    return;

                repeatAudio = value;

                // saving to setting utils
                if (value != null)
                {
                    SettingsUtils.Set(Constants.PREF_AUDIO_REPEAT, value.Value);
                }

                ResetRepeatState();

                base.OnPropertyChanged(() => RepeatAudio);
            }
        }

        #endregion Properties

        #region Audio

        public async Task Play()
        {
            if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Paused)
            {
                QuranApp.NativeProvider.AudioProvider.Play();
            }
            else
            {
                var ayah = SelectedAyah;
                if (ayah == null)
                {
                    var bounds = QuranUtils.GetPageBounds(CurrentPageNumber);
                    ayah = new QuranAyah
                        {
                            Surah = bounds[0],
                            Ayah = bounds[1]
                        };
                    if (ayah.Ayah == 1 && ayah.Surah != Constants.SURA_TAWBA &&
                        ayah.Surah != Constants.SURA_FIRST)
                    {
                        ayah.Ayah = 0;
                    }
                }
                if (QuranUtils.IsValid(ayah))
                {
                    await PlayFromAyah(ayah.Surah, ayah.Ayah);
                }
            }
        }

        public async Task Pause()
        {
            QuranApp.NativeProvider.AudioProvider.Pause();
        }

        private async Task NextTrack()
        {
            var ayah = SelectedAyah;
            if (ayah != null)
            {
                var nextAyah = QuranUtils.GetNextAyah(ayah, false);
                CurrentPageNumber = QuranUtils.GetPageFromAyah(nextAyah);
                SelectedAyah = nextAyah;
                if (AudioPlayerState == AudioState.Playing)
                {
                    Stop();
                    await Play();
                }
            }
        }

        private async Task PreviousTrack()
        {
            var ayah = SelectedAyah;
            if (ayah != null)
            {
                var previousAyah = QuranUtils.GetPreviousAyah(ayah, false);
                CurrentPageNumber = QuranUtils.GetPageFromAyah(previousAyah);
                SelectedAyah = previousAyah;
                if (AudioPlayerState == AudioState.Playing)
                {
                    Stop();
                    await Play();
                }
            }
        }

        public void Stop()
        {
            QuranApp.NativeProvider.AudioProvider.Stop();
        }

        public async Task ResetRepeatState()
        {
            if (AudioPlayerState == AudioState.Playing)
            {
                var position = QuranApp.NativeProvider.AudioProvider.Position;
                Stop();
                await Play();
                QuranApp.NativeProvider.AudioProvider.Position = position;
            }
        }

        public async Task PlayFromAyah(int startSura, int startAyah)
        {
            int currentQari = AudioUtils.GetReciterIdByName(SettingsUtils.Get<string>(Constants.PREF_ACTIVE_QARI));
            if (currentQari == -1)
                return;

            var shouldRepeat = SettingsUtils.Get<bool>(Constants.PREF_AUDIO_REPEAT);
            var repeatAmount = SettingsUtils.Get<RepeatAmount>(Constants.PREF_REPEAT_AMOUNT);
            var repeatTimes = SettingsUtils.Get<int>(Constants.PREF_REPEAT_TIMES);
            var repeat = new RepeatInfo();
            if (shouldRepeat)
            {
                repeat.RepeatAmount = repeatAmount;
                repeat.RepeatCount = repeatTimes;
            }
            var lookaheadAmount = SettingsUtils.Get<AudioDownloadAmount>(Constants.PREF_DOWNLOAD_AMOUNT);
            var ayah = new QuranAyah(startSura, startAyah);
            var request = new AudioRequest(currentQari, ayah, repeat, 0, lookaheadAmount);

            if (SettingsUtils.Get<bool>(Constants.PREF_PREFER_STREAMING))
            {
                PlayStreaming(request);
            }
            else
            {
                await DownloadAndPlayAudioRequest(request);
            }
        }

        private void PlayStreaming(AudioRequest request)
        {
            //TODO: download database

            //TODO: play audio
        }

        private async Task DownloadAndPlayAudioRequest(AudioRequest request)
        {
            if (request == null || this.ActiveDownload.IsDownloading)
            {
                return;
            }

            var result = await DownloadAudioRequest(request);

            if (!result)
            {
                await QuranApp.NativeProvider.ShowErrorMessageBox("Something went wrong. Unable to download audio.");
            }
            else
            {
                var path = AudioUtils.GetLocalPathForAyah(request.CurrentAyah.Ayah == 0 ? new QuranAyah(1, 1) : request.CurrentAyah, request.Reciter);
                var title = request.CurrentAyah.Ayah == 0 ? "Bismillah" : QuranUtils.GetSurahAyahString(request.CurrentAyah);
                QuranApp.NativeProvider.AudioProvider.SetTrack(new Uri(path, UriKind.Relative), title, request.Reciter.Name, "Quran", null,
                    request.ToString());
            }
        }

        private async Task<bool> DownloadAudioRequest(AudioRequest request)
        {
            bool result = true;
            // checking if there is aya position file
            if (!await FileUtils.HaveAyaPositionFile())
            {
                result = await DownloadAyahPositionFile();
            }

            // checking if need to download gapless database file
            if (result && await AudioUtils.ShouldDownloadGaplessDatabase(request))
            {
                string url = request.Reciter.GaplessDatabasePath;
                string destination = request.Reciter.LocalPath;
                // start the download
                result = await this.ActiveDownload.DownloadSingleFile(url, destination, AppResources.loading_data);
            }

            // checking if need to download mp3
            if (result && !await AudioUtils.HaveAllFiles(request))
            {
                string url = request.Reciter.ServerUrl;
                string destination = request.Reciter.LocalPath;
                await FileUtils.EnsureDirectoryExists(destination);

                if (request.Reciter.IsGapless)
                    result = await AudioUtils.DownloadGaplessRange(url, destination, request.FromAyah, request.ToAyah);
                else
                    result = await AudioUtils.DownloadRange(request);
            }
            return result;
        }

        private async void AudioProvider_StateChanged(object sender, EventArgs e)
        {
            if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Stopped ||
                    QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Unknown ||
                QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Error)
            {
                await Task.Delay(500);
                // Check if still stopped
                if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Stopped ||
                    QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Unknown ||
                QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Error)
                {
                    AudioPlayerState = AudioState.Stopped;
                }
            }
            else if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Paused)
            {
                AudioPlayerState = AudioState.Paused;
            }
            else if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Playing)
            {
                AudioPlayerState = AudioState.Playing;

                var track = QuranApp.NativeProvider.AudioProvider.GetTrack();
                if (track != null && track.Tag != null)
                {
                    try
                    {
                        var request = new AudioRequest(track.Tag);
                        var pageNumber = QuranUtils.GetPageFromAyah(request.CurrentAyah);
                        var oldPageIndex = CurrentPageIndex;
                        var newPageIndex = GetIndexFromPageNumber(pageNumber);

                        CurrentPageIndex = newPageIndex;
                        if (oldPageIndex != newPageIndex)
                        {
                            await Task.Delay(500);
                        }
                        // If bismillah set to first ayah
                        if (request.CurrentAyah.Ayah == 0)
                            request.CurrentAyah.Ayah = 1;
                        SelectedAyah = request.CurrentAyah;
                    }
                    catch
                    {
                        // Bad track
                    }
                }
            }
        }

        #endregion
    }
}
