package com.sda.mobile.data

import android.content.Context
import com.sda.mobile.model.AccountMeta
import com.sda.mobile.model.UiMetaStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import java.io.File

class UiMetaRepository(context: Context) {
    private val file = File(File(context.filesDir, "maFiles").apply { if (!exists()) mkdirs() }, "ui-meta.json")
    private val json = Json { ignoreUnknownKeys = true }

    suspend fun load(): UiMetaStore = withContext(Dispatchers.IO) {
        if (!file.exists()) return@withContext UiMetaStore()
        runCatching { json.decodeFromString<UiMetaStore>(file.readText()) }.getOrDefault(UiMetaStore())
    }

    private suspend fun save(store: UiMetaStore) = withContext(Dispatchers.IO) {
        file.writeText(json.encodeToString(UiMetaStore.serializer(), store))
    }

    suspend fun upsert(meta: AccountMeta) {
        val store = load()
        val existingIndex = store.accounts.indexOfFirst { it.steamId == meta.steamId }
        val updated = if (existingIndex >= 0) {
            store.accounts.toMutableList().apply { set(existingIndex, meta) }
        } else {
            store.accounts + meta
        }
        save(store.copy(accounts = updated))
    }

    suspend fun get(steamId64: Long): AccountMeta =
        load().accounts.firstOrNull { it.steamId == steamId64 } ?: AccountMeta(steamId = steamId64)

    suspend fun remove(steamId64: Long) {
        val store = load()
        save(store.copy(accounts = store.accounts.filterNot { it.steamId == steamId64 }))
    }
}
