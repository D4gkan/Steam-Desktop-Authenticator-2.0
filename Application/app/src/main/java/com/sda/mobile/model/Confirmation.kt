package com.sda.mobile.model

import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive

/** Mirrors Confirmation.EMobileConfirmationType on desktop. */
enum class ConfirmationType(val id: Int) {
    INVALID(0),
    TEST(1),
    TRADE(2),
    MARKET_LISTING(3),
    FEATURE_OPT_OUT(4),
    PHONE_NUMBER_CHANGE(5),
    ACCOUNT_RECOVERY(6),

    /** Steam returned a "type" value this build doesn't recognize - shown as-is via [typeName]
     * rather than dropped, matching the desktop app's tolerant RawTypeConverter. */
    UNKNOWN(-1);

    companion object {
        fun fromRaw(raw: String?): ConfirmationType {
            if (raw == null) return INVALID
            raw.toIntOrNull()?.let { n -> return entries.firstOrNull { it.id == n } ?: UNKNOWN }
            return entries.firstOrNull { it.name.equals(raw, ignoreCase = true) } ?: UNKNOWN
        }
    }
}

/** Mirrors SteamAuth.Confirmation on desktop. */
data class Confirmation(
    val id: Long,
    val key: Long,
    val creator: Long,
    val headline: String?,
    val summary: List<String>,
    val accept: String?,
    val cancel: String?,
    val icon: String?,
    val confType: ConfirmationType,
    val typeName: String?
) {
    companion object {
        /** Parses one element of the "conf" array from mobileconf/getlist, tolerating a "type"
         * field that may arrive as either a raw integer or a string - see ConfirmationType. */
        fun fromJson(obj: JsonObject): Confirmation {
            val typeElement = obj["type"]
            val rawType = when {
                typeElement == null -> null
                typeElement is JsonPrimitive -> typeElement.contentOrNull ?: typeElement.intOrNull?.toString()
                else -> null
            }
            return Confirmation(
                id = obj["id"]?.jsonPrimitive?.content?.toLongOrNull() ?: 0L,
                key = obj["nonce"]?.jsonPrimitive?.content?.toLongOrNull() ?: 0L,
                creator = obj["creator_id"]?.jsonPrimitive?.content?.toLongOrNull() ?: 0L,
                headline = obj["headline"]?.jsonPrimitive?.contentOrNull,
                summary = obj["summary"]?.jsonArray?.mapNotNull { it.jsonPrimitive.contentOrNull } ?: emptyList(),
                accept = obj["accept"]?.jsonPrimitive?.contentOrNull,
                cancel = obj["cancel"]?.jsonPrimitive?.contentOrNull,
                icon = obj["icon"]?.jsonPrimitive?.contentOrNull,
                confType = ConfirmationType.fromRaw(rawType),
                typeName = obj["type_name"]?.jsonPrimitive?.contentOrNull
            )
        }
    }
}
