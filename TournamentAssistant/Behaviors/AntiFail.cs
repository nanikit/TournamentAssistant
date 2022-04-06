using System;
using System.Collections;
using System.Linq;
using TournamentAssistant.Utilities;
using TournamentAssistantShared;
using UnityEngine;

/**
 * Created by Moon on 6/13/2020
 * ...and then subsequently left empty until 6/16/2020, 2:40 AM
 */

namespace TournamentAssistant.Behaviors
{
    class AntiFail : MonoBehaviour
    {
        public static AntiFail Instance { get; set; }

        private StandardLevelGameplayManager standardLevelGameplayManager;
        private GameSongController gameSongController;
        private GameEnergyCounter gameEnergyCounter;
        private BeatmapObjectManager beatmapObjectManager;

        private float _nextFrameEnergyChange;
        private float _oldObstacleEnergyDrainPerSecond = 1.3f;
        private bool _wouldHaveFailed = false;

        void Awake()
        {
            Instance = this;

            DontDestroyOnLoad(this); //Will actually be destroyed when the main game scene is loaded again, but unfortunately this 
                                     //object is created before the game scene loads, so we need to do this to prevent the game scene
                                     //load from destroying it

            standardLevelGameplayManager = Resources.FindObjectsOfTypeAll<StandardLevelGameplayManager>().First();

            StartCoroutine(DoOnLevelStart());
        }

        public virtual void LateUpdate()
        {
            if (!_wouldHaveFailed)
            {
                if (gameEnergyCounter != null && gameEnergyCounter.GetField<PlayerHeadAndObstacleInteraction>("_playerHeadAndObstacleInteraction").playerHeadIsInObstacle)
                {
                    AddEnergy(Time.deltaTime * -_oldObstacleEnergyDrainPerSecond);
                }
                if (gameEnergyCounter != null && gameEnergyCounter.GetField<SaberClashChecker>("_saberClashChecker").AreSabersClashing(out var _) && gameEnergyCounter.failOnSaberClash)
                {
                    AddEnergy(-gameEnergyCounter.energy);
                }
                if (!Mathf.Approximately(_nextFrameEnergyChange, 0f))
                {
                    AddEnergy(_nextFrameEnergyChange);
                    _nextFrameEnergyChange = 0f;
                }
            }
        }

        public IEnumerator DoOnLevelStart()
        {
            yield return new WaitUntil(() => standardLevelGameplayManager.GetField<StandardLevelGameplayManager.GameState>("_gameState") == StandardLevelGameplayManager.GameState.Playing);
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<GameEnergyCounter>().Any());

            gameSongController = standardLevelGameplayManager.GetField<GameSongController>("_gameSongController");
            gameEnergyCounter = Resources.FindObjectsOfTypeAll<GameEnergyCounter>().First();

            //Prevent the gameEnergyCounter from invoking death by obstacle
            var eventInfo = gameEnergyCounter.GetType().GetEvent(nameof(gameEnergyCounter.gameEnergyDidReach0Event));
            standardLevelGameplayManager.SetField("_initData", new StandardLevelGameplayManager.InitData(false));

            //Unhook the functions in the energy counter that watch note events, so we can peek inside the process
            beatmapObjectManager = gameEnergyCounter.GetField<BeatmapObjectManager>("_beatmapObjectManager");

            beatmapObjectManager.noteWasMissedEvent -= gameEnergyCounter.HandleNoteWasMissed;
            beatmapObjectManager.noteWasMissedEvent += beatmapObjectManager_noteWasMissedEvent;

            beatmapObjectManager.noteWasCutEvent -= gameEnergyCounter.HandleNoteWasCut;
            beatmapObjectManager.noteWasCutEvent += beatmapObjectManager_noteWasCutEvent;

            //Unhook the level end event so we can reset everything before the level ends
            gameSongController.songDidFinishEvent -= standardLevelGameplayManager.HandleSongDidFinish;
            gameSongController.songDidFinishEvent += gameSongController_songDidFinishEvent;
        }

        private void beatmapObjectManager_noteWasCutEvent(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            if (noteController.noteData.colorType == ColorType.None)
            {
                _nextFrameEnergyChange -= gameEnergyCounter.GetField<float>("kHitBombEnergyDrain");
                return;
            }
            _nextFrameEnergyChange += (noteCutInfo.allIsOK ? gameEnergyCounter.GetField<float>("kGoodNoteEnergyCharge") : (-gameEnergyCounter.GetField<float>("kBadNoteEnergyDrain")));
        }

        private void beatmapObjectManager_noteWasMissedEvent(NoteController noteController)
        {
            if (noteController.noteData.colorType == ColorType.None)
            {
                return;
            }

            var energy = _nextFrameEnergyChange;
            _nextFrameEnergyChange -= gameEnergyCounter.GetField<float>("kMissNoteEnergyDrain");
        }

        //Our custom AddEnergy will pass along the info to the gameEnergyCounter's AddEnergy UNLESS we would have failed, in which case we withhold that information until the end of the level
        private void AddEnergy(float value)
        {
            if (!_wouldHaveFailed)
            {
                var currentEnergy = gameEnergyCounter.energy;

                if (value < 0f)
                {
                    if (currentEnergy <= 0f)
                    {
                        return;
                    }
                    if (gameEnergyCounter.instaFail)
                    {
                        currentEnergy = 0f;
                    }
                    else if (gameEnergyCounter.energyType == GameplayModifiers.EnergyType.Battery)
                    {
                        currentEnergy -= 1f / (float)gameEnergyCounter.GetField<float>("kBatteryLives");
                    }
                    else
                    {
                        currentEnergy += value;
                    }
                    if (currentEnergy <= 1E-05f)
                    {
                        _wouldHaveFailed = true;
                        BS_Utils.Gameplay.ScoreSubmission.DisableSubmission(SharedConstructs.Name); //Probably not necessary since we invoke fail anyway on level end, but just to be safe...
                    }
                }

                if (!_wouldHaveFailed) gameEnergyCounter.ProcessEnergyChange(value);
            }
        }

        private void gameSongController_songDidFinishEvent()
        {
            //Reset the gameEnergyCounter death by obstacle value
            standardLevelGameplayManager.SetField("_initData", new StandardLevelGameplayManager.InitData(true));

            //Rehook the functions in the energy counter that watch note events
            beatmapObjectManager = gameEnergyCounter.GetField<BeatmapObjectManager>("_beatmapObjectManager");

            beatmapObjectManager.noteWasMissedEvent += gameEnergyCounter.HandleNoteWasMissed;
            beatmapObjectManager.noteWasMissedEvent -= beatmapObjectManager_noteWasMissedEvent;

            beatmapObjectManager.noteWasCutEvent += gameEnergyCounter.HandleNoteWasCut;
            beatmapObjectManager.noteWasCutEvent -= beatmapObjectManager_noteWasCutEvent;

            //Rehook the level end event
            gameSongController.songDidFinishEvent += standardLevelGameplayManager.HandleSongDidFinish;
            gameSongController.songDidFinishEvent -= gameSongController_songDidFinishEvent;

            if (_wouldHaveFailed) standardLevelGameplayManager.HandleGameEnergyDidReach0();
            standardLevelGameplayManager.HandleSongDidFinish();
        }

        public static void Destroy() => Destroy(Instance);

        void OnDestroy() => Instance = null;
    }
}