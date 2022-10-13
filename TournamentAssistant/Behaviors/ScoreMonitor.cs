﻿using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using TournamentAssistant.UI.FlowCoordinators;
using TournamentAssistant.Utilities;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantShared.Utilities;
using UnityEngine;

namespace TournamentAssistant.Behaviors
{
    class ScoreMonitor : MonoBehaviour
    {
        public static ScoreMonitor Instance { get; set; }

        private ScoreController _scoreController;
        private GameEnergyCounter _gameEnergyCounter;
        private ComboController _comboController;
        private AudioTimeSyncController _audioTimeSyncController;

        private RoomCoordinator _coordinator;
        private Guid[] destinationUsers;

        private int _lastUpdateScore = 0;
        private int _scoreUpdateFrequency = Plugin.client.State.ServerSettings.ScoreUpdateFrequency;
        private int _scoreCheckDelay = 0;

        // Trackers
        private ScoreTracker _scoreTracker = new ScoreTracker();
        private int[] leftTotalCutScores = { 0, 0, 0 };
        private int[] leftTotalCuts = { 0, 0, 0 };
        private int[] rightTotalCutScores = { 0, 0, 0 };
        private int[] rightTotalCuts = { 0, 0, 0 };

        // Trackers as of last time an update was sent to the server
        private ScoreTracker _lastUpdatedScoreTracker = new ScoreTracker();

        void Awake()
        {
            Instance = this;

            DontDestroyOnLoad(this); //Will actually be destroyed when the main game scene is loaded again, but unfortunately this 
            //object is created before the game scene loads, so we need to do this to prevent the game scene
            //load from destroying it

            StartCoroutine(WaitForComponentCreation());
        }

        public void Update()
        {
            if (_scoreCheckDelay > _scoreUpdateFrequency)
            {
                _scoreCheckDelay = 0;

                if (_scoreController != null && (_scoreController.modifiedScore != _lastUpdateScore
                                                 || _lastUpdatedScoreTracker.notesMissed != _scoreTracker.notesMissed
                                                 || _lastUpdatedScoreTracker.badCuts != _scoreTracker.badCuts
                                                 || _lastUpdatedScoreTracker.bombHits != _scoreTracker.bombHits
                                                 || _lastUpdatedScoreTracker.wallHits != _scoreTracker.wallHits
                                                 || _lastUpdatedScoreTracker.maxCombo != _scoreTracker.maxCombo))
                {
                    ScoreUpdated();

                    _lastUpdateScore = _scoreController.modifiedScore;
                    _lastUpdatedScoreTracker.notesMissed = _scoreTracker.notesMissed;
                    _lastUpdatedScoreTracker.badCuts = _scoreTracker.badCuts;
                    _lastUpdatedScoreTracker.bombHits = _scoreTracker.bombHits;
                    _lastUpdatedScoreTracker.wallHits = _scoreTracker.wallHits;
                    _lastUpdatedScoreTracker.maxCombo = _scoreTracker.maxCombo;
                }
            }

            _scoreCheckDelay++;
        }

        private void ScoreUpdated()
        {
            //Send score update
            var player = Plugin.client.GetUserByGuid(Plugin.client.Self.Guid);

            var accuracy = (float)_scoreController.modifiedScore / _scoreController.immediateMaxPossibleModifiedScore;

            var scoreUpdate = new Push
            {
                realtime_score = new Push.RealtimeScore
                {
                    UserGuid = player.Guid,
                    Score = _scoreController.multipliedScore,
                    ScoreWithModifiers = _scoreController.modifiedScore,
                    Combo = _comboController.GetField<int>("_combo"),
                    Accuracy = float.IsNaN(accuracy) ? 0.00f : accuracy,
                    SongPosition = _audioTimeSyncController.songTime,
                    MaxScore = _scoreController.immediateMaxPossibleMultipliedScore,
                    MaxScoreWithModifiers = _scoreController.immediateMaxPossibleModifiedScore,
                    PlayerHealth = _gameEnergyCounter.energy,
                    scoreTracker = _scoreTracker
                }
            };

            //NOTE: We don't needa be blasting the entire server
            //with score updates. This update will only go out to other
            //players in the current match and the other associated users
            Plugin.client.Send(destinationUsers, new Packet
            {
                Push = scoreUpdate
            });
        }

        public IEnumerator WaitForComponentCreation()
        {
            _coordinator = Resources.FindObjectsOfTypeAll<RoomCoordinator>().FirstOrDefault();
            var match = _coordinator.Match;
            UpdateAudience(match);
            Plugin.client.MatchInfoUpdated += Client_MatchInfoUpdated;
            //new string[] { "x_x" }; //Note to future moon, this will cause the server to receive the forwarding packet and forward it to no one. Since it's received, though, the scoreboard will get it if connected

            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<ScoreController>().Any());
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<ComboController>().Any());
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());

            _scoreController = Resources.FindObjectsOfTypeAll<ScoreController>().First();
            _gameEnergyCounter = Resources.FindObjectsOfTypeAll<GameEnergyCounter>().First();
            _comboController = Resources.FindObjectsOfTypeAll<ComboController>().First();
            _audioTimeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().First();

            yield return new WaitUntil(() => _scoreController.GetField<BeatmapObjectManager>("_beatmapObjectManager") != null);

            var beatmapObjectManager = _scoreController.GetField<BeatmapObjectManager>("_beatmapObjectManager");
            var headObstacleInteration = _scoreController.GetField<PlayerHeadAndObstacleInteraction>("_playerHeadAndObstacleInteraction");
            beatmapObjectManager.noteWasMissedEvent += BeatmapObjectManager_noteWasMissedEvent;
            beatmapObjectManager.noteWasCutEvent += BeatmapObjectManager_noteWasCutEvent;
            _scoreController.scoringForNoteFinishedEvent += ScoreController_scoringForNoteFinishedEvent;
            headObstacleInteration.headDidEnterObstaclesEvent += HeadObstacleInteration_enterObstacle;

            _scoreTracker.leftHand = new ScoreTrackerHand();
            _scoreTracker.leftHand.avgCuts = new float[3] { 0, 0, 0 };
            _scoreTracker.rightHand = new ScoreTrackerHand();
            _scoreTracker.rightHand.avgCuts = new float[3] { 0, 0, 0 };
        }

        private void UpdateAudience(Match match)
        {
            TournamentAssistantShared.Logger.Info($"Update audience by match GUID: {match.Guid}");
            destinationUsers = ((bool)(_coordinator?.TournamentMode) && !Plugin.UseFloatingScoreboard)
                ? match.AssociatedUsers.Where(x => Plugin.client.GetUserByGuid(x).ClientType != User.ClientTypes.Player).Select(x => Guid.Parse(x)).ToArray()
                : match.AssociatedUsers.Select(x => Guid.Parse(x))
                    .ToArray(); //We don't wanna be doing this every frame
        }

        private Task Client_MatchInfoUpdated(Match match)
        {
            TournamentAssistantShared.Logger.Info($"Match update received: {match.Guid}, current match guid: {_coordinator.Match.Guid}");
            if (match.Guid == _coordinator.Match.Guid)
            {
                UpdateAudience(match);
            }
            return Task.CompletedTask;
        }

        private void BeatmapObjectManager_noteWasMissedEvent(NoteController noteController)
        {
            if (noteController.noteData.gameplayType == NoteData.GameplayType.Bomb)
            {
                return;
            }
            _scoreTracker.notesMissed++;
            if (noteController.noteData.colorType == ColorType.ColorA)
            {
                _scoreTracker.leftHand.Miss++;
            }
            else if (noteController.noteData.colorType == ColorType.ColorB)
            {
                _scoreTracker.rightHand.Miss++;
            }
        }

        private void ScoreController_scoringForNoteFinishedEvent(ScoringElement scoringElement)
        {
            if (scoringElement is GoodCutScoringElement goodCut)
            {
                var cutScoreBuffer = goodCut.cutScoreBuffer;

                var beforeCut = cutScoreBuffer.beforeCutScore;
                var afterCut = cutScoreBuffer.afterCutScore;
                var cutDistance = cutScoreBuffer.centerDistanceCutScore;
                var fixedScore = cutScoreBuffer.noteScoreDefinition.fixedCutScore;

                var totalScoresForHand = goodCut.noteData.colorType == ColorType.ColorA ? leftTotalCutScores : rightTotalCutScores;

                var cutCountForHand = goodCut.noteData.colorType == ColorType.ColorA ? leftTotalCuts : rightTotalCuts;

                switch (goodCut.noteData.scoringType)
                {
                    case NoteData.ScoringType.Normal:
                        totalScoresForHand[0] += beforeCut;
                        totalScoresForHand[1] += afterCut;
                        totalScoresForHand[2] += cutDistance;

                        cutCountForHand[0]++;
                        cutCountForHand[1]++;
                        cutCountForHand[2]++;
                        break;
                    case NoteData.ScoringType.SliderHead:
                        totalScoresForHand[0] += beforeCut;
                        totalScoresForHand[2] += cutDistance;

                        cutCountForHand[0]++;
                        cutCountForHand[2]++;
                        break;
                    case NoteData.ScoringType.SliderTail:
                        totalScoresForHand[1] += afterCut;
                        totalScoresForHand[2] += cutDistance;

                        cutCountForHand[1]++;
                        cutCountForHand[2]++;
                        break;
                    case NoteData.ScoringType.BurstSliderHead:
                        totalScoresForHand[0] += beforeCut;
                        totalScoresForHand[2] += cutDistance;

                        cutCountForHand[0]++;
                        cutCountForHand[2]++;
                        break;
                }

                if (goodCut.noteData.colorType == ColorType.ColorA)
                {
                    _scoreTracker.leftHand.avgCuts[0] = totalScoresForHand[0] / cutCountForHand[0];
                    _scoreTracker.leftHand.avgCuts[1] = totalScoresForHand[1] / cutCountForHand[1];
                    _scoreTracker.leftHand.avgCuts[2] = totalScoresForHand[2] / cutCountForHand[2];
                }
                else if (goodCut.noteData.colorType == ColorType.ColorB)
                {
                    _scoreTracker.rightHand.avgCuts[0] = totalScoresForHand[0] / cutCountForHand[0];
                    _scoreTracker.rightHand.avgCuts[1] = totalScoresForHand[1] / cutCountForHand[1];
                    _scoreTracker.rightHand.avgCuts[2] = totalScoresForHand[2] / cutCountForHand[2];
                }

                var combo = _comboController.GetField<int>("_combo");
                if (combo > _scoreTracker.maxCombo)
                {
                    _scoreTracker.maxCombo = combo;
                }
            }
        }

        private void BeatmapObjectManager_noteWasCutEvent(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            if (noteCutInfo.noteData.scoringType == NoteData.ScoringType.Ignore)
            {
                return;
            }

            if (noteCutInfo.allIsOK)
            {
                if (noteController.noteData.colorType == ColorType.ColorA)
                {
                    _scoreTracker.leftHand.Hit++;
                }
                else if (noteController.noteData.colorType == ColorType.ColorB)
                {
                    _scoreTracker.rightHand.Hit++;
                }
            }
            else if (!noteCutInfo.allIsOK && noteCutInfo.noteData.gameplayType != NoteData.GameplayType.Bomb)
            {
                _scoreTracker.badCuts++;
                if (noteController.noteData.colorType == ColorType.ColorA)
                {
                    _scoreTracker.leftHand.badCut++;
                }
                else if (noteController.noteData.colorType == ColorType.ColorB)
                {
                    _scoreTracker.rightHand.badCut++;
                }
            }
            else if (noteCutInfo.noteData.gameplayType == NoteData.GameplayType.Bomb)
            {
                _scoreTracker.bombHits++;
            }
        }

        private void HeadObstacleInteration_enterObstacle()
        {
            _scoreTracker.wallHits++;
        }

        public static void Destroy() => Destroy(Instance);

        void OnDestroy()
        {
            var beatmapObjectManager = _scoreController.GetField<BeatmapObjectManager>("_beatmapObjectManager");
            var headObstacleInteration = _scoreController.GetField<PlayerHeadAndObstacleInteraction>("_playerHeadAndObstacleInteraction");
            beatmapObjectManager.noteWasMissedEvent -= BeatmapObjectManager_noteWasMissedEvent;
            beatmapObjectManager.noteWasCutEvent -= BeatmapObjectManager_noteWasCutEvent;
            headObstacleInteration.headDidEnterObstaclesEvent -= HeadObstacleInteration_enterObstacle;
            Plugin.client.MatchInfoUpdated -= Client_MatchInfoUpdated;
            Instance = null;
        }
    }
}