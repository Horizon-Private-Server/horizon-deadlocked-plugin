using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;

public static class ReflectionHelper
{
    public static string GetDescription(this Enum value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var field = value.GetType().GetField(value.ToString());
        var attribute = field?.GetCustomAttribute<DescriptionAttribute>();

        return attribute?.Description ?? value.ToString();
    }
}
