package com.sda.mobile.ui.screens

import android.graphics.Bitmap
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.google.zxing.BarcodeFormat
import com.google.zxing.qrcode.QRCodeWriter
import com.sda.mobile.ui.viewmodel.AppViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun QrExportScreen(steamId64: Long, onBack: () -> Unit, viewModel: AppViewModel) {
    val state by viewModel.state.collectAsState()
    val account = state.accounts.firstOrNull { it.account.steamId64 == steamId64 }?.account

    Scaffold(topBar = {
        TopAppBar(
            title = { Text("Export as QR") },
            navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, contentDescription = "Back") } }
        )
    }) { padding ->
        Column(
            Modifier.fillMaxSize().padding(padding).padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            if (account == null) {
                Text("Account not found.")
                return@Column
            }

            Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                Row(Modifier.padding(16.dp), verticalAlignment = Alignment.Top) {
                    Icon(Icons.Default.Warning, contentDescription = null)
                    Spacer(Modifier.width(12.dp))
                    Text(
                        "This QR code contains your full Steam Guard secret in plain text (unencrypted, regardless of whether this app's local storage is encrypted). Anyone who scans it can generate your 2FA codes and approve your trade/market confirmations. Only show it to a device you control, then close this screen.",
                        style = MaterialTheme.typography.bodyMedium
                    )
                }
            }

            Spacer(Modifier.height(24.dp))

            val plaintext = remember(account) { viewModel.exportAccountPlaintext(account) }
            val bitmap = remember(plaintext) { generateQrBitmap(plaintext, 800) }

            if (bitmap == null) {
                Text(
                    "This account's data is too large to fit in a single QR code. Use file-based import/export instead.",
                    color = MaterialTheme.colorScheme.error
                )
            } else {
                Image(bitmap.asImageBitmap(), contentDescription = "QR code for ${account.accountName}", modifier = Modifier.fillMaxWidth())
            }

            Spacer(Modifier.height(16.dp))
            Text(account.accountName ?: "", style = MaterialTheme.typography.titleMedium)
        }
    }
}

private fun generateQrBitmap(text: String, sizePx: Int): Bitmap? = try {
    val matrix = QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, sizePx, sizePx)
    val bitmap = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.RGB_565)
    for (x in 0 until sizePx) {
        for (y in 0 until sizePx) {
            bitmap.setPixel(x, y, if (matrix[x, y]) android.graphics.Color.BLACK else android.graphics.Color.WHITE)
        }
    }
    bitmap
} catch (e: Exception) {
    // Most likely com.google.zxing.WriterException: Data too big - a .maFile is usually well
    // under the ~2.9KB a version-40/L QR code can hold, but this keeps the screen from
    // crashing on an unusually large one instead of showing a friendly message.
    null
}
