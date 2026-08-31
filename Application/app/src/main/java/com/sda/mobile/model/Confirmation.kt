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

/** Mirrors SteamAuth.Confirmation on desktop.
 *
 * [id] and [key] are kept as the raw decimal strings Steam sent, not parsed into Long: Steam's
 * "id" and (especially) "nonce" values are opaque unsigned 64-bit tokens that routinely exceed
 * Long.MAX_VALUE. Parsing them with toLongOrNull() silently returns null on overflow, which was
 * defaulting [key] to 0 - the confirmation would still *display* fine (nothing here needs the
 * real value), but every accept/deny call echoed "ck=0" back to Steam instead of the real nonce,
 * so Steam correctly rejected it (success:false) and the UI surfaced that as "Steam rejected the
 * approval/denial. Try again." for every confirmation, every time. */
data class Confirmation(
    val id: String,
    val key: String,
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
                id = obj["id"]?.jsonPrimitive?.contentOrNull ?: "",
                key = obj["nonce"]?.jsonPrimitive?.contentOrNull ?: "",
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
