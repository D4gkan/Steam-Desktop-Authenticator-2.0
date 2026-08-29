using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SteamAuth
{
    public class Confirmation
    {
        [JsonProperty(PropertyName = "id")]
        public ulong ID { get; set; }

        [JsonProperty(PropertyName = "nonce")]
        public ulong Key { get; set; }

        [JsonProperty(PropertyName = "creator_id")]
        public ulong Creator { get; set; }

        [JsonProperty(PropertyName = "headline")]
        public string Headline { get; set; }

        [JsonProperty(PropertyName = "summary")]
        public List<String> Summary { get; set; }

        [JsonProperty(PropertyName = "accept")]
        public string Accept { get; set; }

        [JsonProperty(PropertyName = "cancel")]
        public string Cancel { get; set; }

        [JsonProperty(PropertyName = "icon")]
        public string Icon { get; set; }

        /// <summary>Raw type string exactly as Steam sent it (e.g. "trade", "market_sell_transaction",
        /// or something SDA has never seen before). Kept alongside ConfType so a confirmation is
        /// never silently dropped just because it doesn't map to a known enum value - see
        /// EMobileConfirmationType.Unknown and RawTypeConverter below.</summary>
        [JsonProperty(PropertyName = "type")]
        [JsonConverter(typeof(RawTypeConverter))]
        public EMobileConfirmationType ConfType { get; set; } = EMobileConfirmationType.Invalid;

        [JsonProperty(PropertyName = "type_name")]
        public string TypeName { get; set; }

        public enum EMobileConfirmationType
        {
            Invalid = 0,
            Test = 1,
            Trade = 2,
            MarketListing = 3,
            FeatureOptOut = 4,
            PhoneNumberChange = 5,
            AccountRecovery = 6,

            /// <summary>Steam returned a "type" value this build of SDA does not recognize.
            /// The original (crashing) behavior was to let Newtonsoft's StringEnumConverter
            /// throw a JsonSerializationException for any unmapped value, which took down the
            /// entire confirmation fetch for every account being polled - not just the one
            /// unrecognized item. Falling back to Unknown instead means one new/unfamiliar
            /// confirmation type from Steam can no longer hide every other pending confirmation
            /// behind an exception (Task 12: unknown types must be visible, not silently lost,
            /// and must not break the rest of the list).</summary>
            Unknown = -1
        }

        /// <summary>Tolerant replacement for Newtonsoft's StringEnumConverter: maps a known type
        /// string/number to its enum value, and anything else to Unknown instead of throwing.
        /// Steam has sent both string names (e.g. "trade") and small integers historically, so
        /// both are handled.</summary>
        private class RawTypeConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(EMobileConfirmationType);

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);

                if (token.Type == JTokenType.Integer)
                {
                    int intValue = token.Value<int>();
                    return Enum.IsDefined(typeof(EMobileConfirmationType), intValue)
                        ? (EMobileConfirmationType)intValue
                        : EMobileConfirmationType.Unknown;
                }

                string stringValue = token.Value<string>() ?? string.Empty;
                if (Enum.TryParse(stringValue, ignoreCase: true, out EMobileConfirmationType parsed)
                    && Enum.IsDefined(typeof(EMobileConfirmationType), parsed))
                {
                    return parsed;
                }

                return EMobileConfirmationType.Unknown;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteValue(value?.ToString() ?? EMobileConfirmationType.Invalid.ToString());
            }
        }
    }

    public class ConfirmationsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("needauth")]
        public bool NeedAuthentication { get; set; }

        [JsonProperty("conf")]
        public Confirmation[] Confirmations { get; set; }
    }
}
