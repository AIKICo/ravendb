﻿using System.IO;
using Newtonsoft.Json;


namespace BlittableTests
{
    public static class StringExtentions
    {
        public static string ToJsonString(this object self)
        {
            var jsonSerializer = new JsonSerializer();
            var stringWriter = new StringWriter();
            var jsonWriter = new JsonTextWriter(stringWriter);
            jsonSerializer.Serialize(jsonWriter, self);

            return stringWriter.ToString();
        }
    }
}
