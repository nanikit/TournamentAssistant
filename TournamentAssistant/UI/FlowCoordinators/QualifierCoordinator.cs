﻿#pragma warning disable IDE0052
using BeatSaberMarkupLanguage;
using HMUI;
using System;
using System.Linq;
using TournamentAssistant.Misc;
using TournamentAssistant.UI.ViewControllers;
using TournamentAssistant.Utilities;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using UnityEngine;
using static TournamentAssistantShared.Models.GameplayModifiers;
using static TournamentAssistantShared.Models.PlayerSpecificSettings;

namespace TournamentAssistant.UI.FlowCoordinators
{
    class QualifierCoordinator : FlowCoordinator
    {
        public event Action DidFinishEvent;

        public QualifierEvent Event { get; set; }
        public CoreServer EventHost { get; set; }

        private SongSelection _songSelection;
        private SongDetail _songDetail;

        private GameplayParameters _currentParameters;
        private IBeatmapLevel _lastPlayedBeatmapLevel;
        private BeatmapCharacteristicSO _lastPlayedCharacteristic;
        private BeatmapDifficulty _lastPlayedDifficulty;

        private PlayerDataModel _playerDataModel;
        private MenuLightsManager _menuLightsManager;
        private SoloFreePlayFlowCoordinator _soloFreePlayFlowCoordinator;
        private CampaignFlowCoordinator _campaignFlowCoordinator;

        private PlatformLeaderboardViewController _globalLeaderboard;
        private CustomLeaderboard _customLeaderboard;
        private ResultsViewController _resultsViewController;
        private MenuLightsPresetSO _scoreLights;
        private MenuLightsPresetSO _redLights;
        private MenuLightsPresetSO _defaultLights;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Qualifier Room", ViewController.AnimationType.None);
                showBackButton = true;

                _playerDataModel = Resources.FindObjectsOfTypeAll<PlayerDataModel>().First();
                _menuLightsManager = Resources.FindObjectsOfTypeAll<MenuLightsManager>().First();
                _soloFreePlayFlowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().First();
                _campaignFlowCoordinator = Resources.FindObjectsOfTypeAll<CampaignFlowCoordinator>().First();
                _resultsViewController = Resources.FindObjectsOfTypeAll<ResultsViewController>().First();
                _scoreLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_resultsClearedLightsPreset");
                _redLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_resultsFailedLightsPreset");
                _defaultLights = _soloFreePlayFlowCoordinator.GetField<MenuLightsPresetSO>("_defaultLightsPreset");

                _songSelection = BeatSaberUI.CreateViewController<SongSelection>();
                _songSelection.SongSelected += SongSelection_SongSelected;

                _songDetail = BeatSaberUI.CreateViewController<SongDetail>();
                _songDetail.PlayPressed += SongDetail_didPressPlayButtonEvent;
                _songDetail.DisableCharacteristicControl = true;
                _songDetail.DisableDifficultyControl = true;
                _songDetail.DisablePlayButton = false;

                _customLeaderboard = BeatSaberUI.CreateViewController<CustomLeaderboard>();
            }
            if (addedToHierarchy)
            {
                _songSelection.SetSongs(Event.QualifierMaps.ToList());
                ProvideInitialViewControllers(_songSelection);
            }
        }

        private void SongDetail_didPressPlayButtonEvent(IBeatmapLevel level, BeatmapCharacteristicSO characteristic, BeatmapDifficulty difficulty)
        {
            _lastPlayedBeatmapLevel = level;
            _lastPlayedCharacteristic = characteristic;
            _lastPlayedDifficulty = difficulty;

            var playerData = Resources.FindObjectsOfTypeAll<PlayerDataModel>().First().playerData;
            var playerSettings = playerData.playerSpecificSettings;

            //Override defaults if we have forced options enabled
            if (_currentParameters.PlayerSettings.Options != PlayerOptions.None)
            {
                playerSettings = new PlayerSpecificSettings(
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.LeftHanded),
                        _currentParameters.PlayerSettings.PlayerHeight,
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AutoPlayerHeight),
                        _currentParameters.PlayerSettings.SfxVolume,
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.ReduceDebris),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.NoHud),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.NoFailEffects),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AdvancedHud),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AutoRestart),
                        _currentParameters.PlayerSettings.SaberTrailIntensity,
                        _currentParameters.PlayerSettings.GetField<NoteJumpDurationTypeSettings>("noteJumpDurationTypeSettings"),
                        _currentParameters.PlayerSettings.GetField<float>("noteJumpFixedDuration"),
                        _currentParameters.PlayerSettings.NoteJumpStartBeatOffset,
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.HideNoteSpawnEffect),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.AdaptiveSfx),
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.StaticLights) ? EnvironmentEffectsFilterPreset.NoEffects : EnvironmentEffectsFilterPreset.AllEffects,
                        _currentParameters.PlayerSettings.Options.HasFlag(PlayerOptions.StaticLights) ? EnvironmentEffectsFilterPreset.NoEffects : EnvironmentEffectsFilterPreset.AllEffects
                    );
            }

            var songSpeed = GameplayModifiers.SongSpeed.Normal;
            if (_currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.SlowSong)) songSpeed = GameplayModifiers.SongSpeed.Slower;
            if (_currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.FastSong)) songSpeed = GameplayModifiers.SongSpeed.Faster;
            if (_currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.SuperFastSong)) songSpeed = GameplayModifiers.SongSpeed.SuperFast;

            var gameplayModifiers = new GameplayModifiers(
                noFailOn0Energy: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoFail),
                energyType: _currentParameters.GameplayModifiers.Options switch
                {
                    var options when options.HasFlag(GameOptions.BatteryEnergy) => GameplayModifiers.EnergyType.Battery,
                    _ => GameplayModifiers.EnergyType.Bar,
                },
                instaFail: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.InstaFail),
                failOnSaberClash: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.FailOnClash),
                enabledObstacleType: _currentParameters.GameplayModifiers.Options switch
                {
                    var options when options.HasFlag(GameOptions.NoObstacles) => GameplayModifiers.EnabledObstacleType.NoObstacles,
                    var options when options.HasFlag(GameOptions.DemoNoObstacles) => GameplayModifiers.EnabledObstacleType.FullHeightOnly,
                    _ => GameplayModifiers.EnabledObstacleType.All,
                },
                noBombs: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoBombs),
                fastNotes: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.FastNotes),
                strictAngles: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.StrictAngles),
                disappearingArrows: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.DisappearingArrows),
                songSpeed: songSpeed,
                noArrows: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.NoArrows),
                ghostNotes: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.GhostNotes),
                proMode: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.ProMode),
                zenMode: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.ZenMode),
                smallCubes: _currentParameters.GameplayModifiers.Options.HasFlag(GameOptions.SmallCubes)
            );

            var colorScheme = playerData.colorSchemesSettings.overrideDefaultColors ? playerData.colorSchemesSettings.GetSelectedColorScheme() : null;

            //Disable scores if we need to
            if (((QualifierEvent.EventSettings)Event.Flags).HasFlag(QualifierEvent.EventSettings.DisableScoresaberSubmission)) BS_Utils.Gameplay.ScoreSubmission.DisableSubmission(SharedConstructs.Name);

            SongUtils.PlaySong(level, characteristic, difficulty, playerData.overrideEnvironmentSettings, colorScheme, gameplayModifiers, playerSettings, SongFinished);
        }

        private void SongSelection_SongSelected(GameplayParameters parameters)
        {
            _currentParameters = parameters;

            SongUtils.LoadSong(parameters.Beatmap.LevelId, (loadedLevel) =>
            {
                PresentViewController(_songDetail, async () =>
                {
                    await _songDetail.SetSelectedSong(loadedLevel);
                    await _songDetail.SetSelectedDifficulty((int)parameters.Beatmap.Difficulty);
                    await _songDetail.SetSelectedCharacteristic(parameters.Beatmap.Characteristic.SerializedName);

                    if (_globalLeaderboard == null)
                    {
                        _globalLeaderboard = Resources.FindObjectsOfTypeAll<PlatformLeaderboardViewController>().First();
                        _globalLeaderboard.name = "Global Leaderboard";
                    }

                    _globalLeaderboard.SetData(SongUtils.GetClosestDifficultyPreferLower(loadedLevel, (BeatmapDifficulty)(int)parameters.Beatmap.Difficulty, parameters.Beatmap.Characteristic.SerializedName));
                    SetRightScreenViewController(_globalLeaderboard, ViewController.AnimationType.In);

                    PlayerUtils.GetPlatformUserData(RequestLeaderboardWhenResolved);
                    SetLeftScreenViewController(_customLeaderboard, ViewController.AnimationType.In);
                });
            });
        }

        private void ResultsViewController_continueButtonPressedEvent(ResultsViewController results)
        {
            _resultsViewController.continueButtonPressedEvent -= ResultsViewController_continueButtonPressedEvent;
            _menuLightsManager.SetColorPreset(_defaultLights, true);
            DismissViewController(_resultsViewController);
        }

        private void ResultsViewController_restartButtonPressedEvent(ResultsViewController results)
        {
            _resultsViewController.continueButtonPressedEvent -= ResultsViewController_continueButtonPressedEvent;
            _menuLightsManager.SetColorPreset(_defaultLights, true);
            DismissViewController(_resultsViewController, finishedCallback: () => SongDetail_didPressPlayButtonEvent(_lastPlayedBeatmapLevel, _lastPlayedCharacteristic, _lastPlayedDifficulty));
        }

        public void SongFinished(StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData, LevelCompletionResults results)
        {
            standardLevelScenesTransitionSetupData.didFinishEvent -= SongFinished;

            var map = standardLevelScenesTransitionSetupData.difficultyBeatmap;
            var localPlayer = _playerDataModel.playerData;
            var localResults = localPlayer.GetPlayerLevelStatsData(map.level.levelID, map.difficulty, map.parentDifficultyBeatmapSet.beatmapCharacteristic);
            var highScore = localResults.highScore < results.modifiedScore;

            if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart) SongDetail_didPressPlayButtonEvent(_lastPlayedBeatmapLevel, _lastPlayedCharacteristic, _lastPlayedDifficulty);
            else if (results.levelEndStateType != LevelCompletionResults.LevelEndStateType.Incomplete)
            {
                if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared)
                {
                    PlayerUtils.GetPlatformUserData((username, userId) => SubmitScoreWhenResolved(username, userId, results));

                    _menuLightsManager.SetColorPreset(_scoreLights, true);
                    _resultsViewController.Init(results, standardLevelScenesTransitionSetupData.transformedBeatmapData, map, false, highScore);
                    _resultsViewController.continueButtonPressedEvent += ResultsViewController_continueButtonPressedEvent;
                    _resultsViewController.restartButtonPressedEvent += ResultsViewController_restartButtonPressedEvent;
                }
                else if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
                {
                    _menuLightsManager.SetColorPreset(_redLights, true);
                    _resultsViewController.Init(results, standardLevelScenesTransitionSetupData.transformedBeatmapData, map, false, highScore);
                    _resultsViewController.continueButtonPressedEvent += ResultsViewController_continueButtonPressedEvent;
                    _resultsViewController.restartButtonPressedEvent += ResultsViewController_restartButtonPressedEvent;
                }

                PresentViewController(_resultsViewController, immediately: true);
            }
        }

        private async void SubmitScoreWhenResolved(string username, ulong userId, LevelCompletionResults results)
        {
            var scores = ((await HostScraper.RequestResponse(EventHost, new Packet(new SubmitScore
            {
                Score = new Score
                {
                    EventId = Event.EventId,
                    Parameters = _currentParameters,
                    UserId = userId,
                    Username = username,
                    FullCombo = results.fullCombo,
                    _Score = results.modifiedScore,
                    Color = "#ffffff"
                }
            }), typeof(ScoreRequestResponse), username, userId)).SpecificPacket as ScoreRequestResponse).Scores.Take(10).ToArray();

            UnityMainThreadDispatcher.Instance().Enqueue(() => SetCustomLeaderboardScores(scores, userId));
        }

        private async void RequestLeaderboardWhenResolved(string username, ulong userId)
        {
            var scores = ((await HostScraper.RequestResponse(EventHost, new Packet(new ScoreRequest
            {
                EventId = Event.EventId,
                Parameters = _currentParameters
            }), typeof(ScoreRequestResponse), username, userId)).SpecificPacket as ScoreRequestResponse).Scores.Take(10).ToArray();

            UnityMainThreadDispatcher.Instance().Enqueue(() => SetCustomLeaderboardScores(scores, userId));
        }

        public void SetCustomLeaderboardScores(Score[] scores, ulong userId)
        {
            var place = 1;
            var indexOfme = -1;
            _customLeaderboard.SetScores(scores.Select(x =>
            {
                if (x.UserId == userId) indexOfme = place - 1;
                return new LeaderboardTableView.ScoreData(x._Score, x.Username, place++, x.FullCombo);
            }).ToList(), indexOfme);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            if (_resultsViewController.isInViewControllerHierarchy)
            {
                _menuLightsManager.SetColorPreset(_defaultLights, false);
                DismissViewController(_resultsViewController);
            }
            else if (_songDetail.isInViewControllerHierarchy)
            {
                SetLeftScreenViewController(null, ViewController.AnimationType.Out);
                SetRightScreenViewController(null, ViewController.AnimationType.Out);
                DismissViewController(_songDetail);
            }
            else DidFinishEvent?.Invoke();
        }
    }
}
