using System;
using BepInEx;
using BepInEx.Configuration;

namespace TaskItemIndicator
{
    [BepInPlugin("com.hj.taskitemindicator", "Task Item Indicator", "1.0.2")]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _standalone;
        private readonly QuestPointer _pointer = new QuestPointer();
        private bool _loggedError;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Show the task item indicator in raid.");
            _standalone = Config.Bind("General", "Show Standalone Task Items", true,
                "Also point at quest items no task asks for (e.g. lore notes / tapes from other mods).");
            _pointer.Log = message => Logger.LogInfo(message);
            Logger.LogInfo("Task Item Indicator loaded.");
        }

        private void Update()
        {
            try
            {
                if (_enabled.Value)
                {
                    _pointer.ShowStandaloneTaskItems = _standalone.Value;
                    _pointer.Tick();
                }
                else
                {
                    _pointer.Remove();
                }
            }
            catch (Exception error)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Logger.LogError(error);
                }
            }
        }
    }
}
