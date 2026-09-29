using System;
using BepInEx;
using BepInEx.Configuration;

namespace TaskItemIndicator
{
    [BepInPlugin("com.hj.taskitemindicator", "Task Item Indicator", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _enabled;
        private readonly QuestPointer _pointer = new QuestPointer();
        private bool _loggedError;

        private void Awake()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Show the task item indicator in raid.");
            _pointer.Log = message => Logger.LogInfo(message);
            Logger.LogInfo("Task Item Indicator loaded.");
        }

        private void Update()
        {
            try
            {
                if (_enabled.Value)
                {
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
