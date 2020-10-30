﻿using UnityEngine;

namespace i5.Toolkit.Core.AppConsole
{
    public abstract class LogColorFormatter : ConsoleFormatterBase
    {
        [SerializeField] protected Color logColor = Color.white;
        [SerializeField] protected Color warningColor = Color.yellow;
        [SerializeField] protected Color errorColor = Color.red;
        [SerializeField] protected Color exceptionColor = Color.red;
        [SerializeField] protected Color assertColor = Color.red;

        protected new ILogColorFormatterLogic logFormatterLogic;
    }
}