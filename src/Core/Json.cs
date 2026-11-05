using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace FMGPatcher.Core
{
    /// <summary>用系统自带的 DataContractJsonSerializer，不额外带 DLL。</summary>
    public static class Json
    {
        static DataContractJsonSerializer Serializer<T>() =>
            new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static T Load<T>(string path)
        {
            using (var fs = File.OpenRead(path)) return (T)Serializer<T>().ReadObject(fs);
        }

        public static void Save<T>(string path, T value)
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true, "  "))
            {
                Serializer<T>().WriteObject(w, value);
                w.Flush();
            }
            FileOps.ReplaceFile(tmp, path);
        }
    }
}
