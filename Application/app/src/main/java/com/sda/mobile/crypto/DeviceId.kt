package com.sda.mobile.crypto

import java.util.UUID

/** Matches AuthenticatorLinker.GenerateDeviceID() on desktop - "android:{uuid}". */
object DeviceId {
    fun generate(): String = "android:" + UUID.randomUUID().toString()
}
