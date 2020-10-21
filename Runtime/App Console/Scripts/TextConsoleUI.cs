﻿using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace i5.Toolkit.Core.SceneConsole
{
    public class TextConsoleUI : ConsoleUIBase
    {
        [SerializeField] private TextMeshProUGUI consoleTextDisplay;

        public override void UpdateUI(List<INotificationMessage> notificationMessages)
        {
            base.UpdateUI(notificationMessages);

            string text = "";
            for (int i = 0; i < notificationMessages.Count; i++)
            {
                text += consoleFormatter.Format(notificationMessages[i]);
                if (i < notificationMessages.Count - 1)
                {
                    text += Environment.NewLine;
                }
            }

            consoleTextDisplay.text = text;
        }
    }
}