using Sanicball.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Sanicball
{
    public class Startup : MonoBehaviour
    {
        public UI.Intro intro;
        public CanvasGroup setNicknameGroup;
        public InputField nicknameField;

        public void ValidateNickname()
        {
            var trimmed = nicknameField.text.Trim();
            if (trimmed != "")
            {
                setNicknameGroup.alpha = 0f;
                ActiveData.GameSettings.nickname = trimmed;
                ActiveData.SaveAllData();
                intro.enabled = true;
            }
        }

        private void Start()
        {
            // Always prompt for a nickname on startup and prefill if one exists
            setNicknameGroup.alpha = 1f;
            intro.enabled = false;
            if (nicknameField != null)
            {
                nicknameField.text = ActiveData.GameSettings.nickname ?? string.Empty;
            }
        }
    }
}