using System.IO;
using System.Text;
using UnityEngine;

namespace ErenshorDeepSims
{
    internal static class JsonUtil
    {
        internal static T ReadFile<T>(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            return JsonUtility.FromJson<T>(text);
        }

        internal static JsonWriteObservation WriteFile<T>(string path, T value)
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string text = JsonUtility.ToJson(value, true);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return new JsonWriteObservation { Fields = JsonFieldPresenceObservation.Inspect(text) };
        }

        internal static T Clone<T>(T value)
        {
            if (object.ReferenceEquals(value, null)) return default(T);
            return JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
