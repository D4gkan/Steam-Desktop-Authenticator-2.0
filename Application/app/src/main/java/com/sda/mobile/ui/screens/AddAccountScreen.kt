package com.sda.mobile.ui.screens

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.sda.mobile.ui.viewmodel.AppViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AddAccountScreen(onDone: () -> Unit, onStartLogin: () -> Unit, viewModel: AppViewModel) {
    val context = LocalContext.current
    var error by remember { mutableStateOf<String?>(null) }
    var busy by remember { mutableStateOf(false) }

    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri: Uri? ->
        if (uri == null) return@rememberLauncherForActivityResult
        busy = true
        val text = context.contentResolver.openInputStream(uri)?.bufferedReader()?.use { it.readText() }
        if (text == null) {
            busy = false
            error = "Could not read that file."
            return@rememberLauncherForActivityResult
        }
        viewModel.importPlaintextMaFile(text) { result ->
            busy = false
            result.onSuccess { onDone() }.onFailure { error = it.message ?: "Import failed." }
        }
    }

    val qrScanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val contents = result.contents ?: return@rememberLauncherForActivityResult
        busy = true
        viewModel.importPlaintextMaFile(contents) { res ->
            busy = false
            res.onSuccess { onDone() }.onFailure { error = it.message ?: "That QR code wasn't a valid .maFile." }
        }
    }

    Scaffold(topBar = {
        TopAppBar(title = { Text("Add an account") })
    }) { padding ->
        Column(Modifier.padding(padding).padding(20.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            error?.let {
                Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyMedium)
            }
            if (busy) LinearProgressIndicator(Modifier.fillMaxWidth())

            OptionCard(
                icon = Icons.Default.FileOpen,
                title = "Import a .maFile",
                subtitle = "Choose an existing .maFile you've transferred to this phone (e.g. via USB or a cloud drive)",
                onClick = { filePicker.launch(arrayOf("*/*")) }
            )
            OptionCard(
                icon = Icons.Default.QrCodeScanner,
                title = "Scan QR from desktop",
                subtitle = "Use SDA desktop's \"Export as QR\" feature, then scan the code here",
                onClick = {
                    qrScanner.launch(
                        ScanOptions()
                            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                            .setPrompt("Scan the QR code shown in SDA desktop")
                            .setBeepEnabled(false)
                    )
                }
            )
            OptionCard(
                icon = Icons.Default.Login,
                title = "Log in to link a new authenticator",
                subtitle = "For a Steam account that doesn't have Steam Guard Mobile set up yet",
                onClick = onStartLogin
            )
        }
    }
}

@Composable
private fun OptionCard(icon: androidx.compose.ui.graphics.vector.ImageVector, title: String, subtitle: String, onClick: () -> Unit) {
    ElevatedCard(onClick = onClick, modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(16.dp), verticalAlignment = androidx.compose.ui.Alignment.CenterVertically) {
            Icon(icon, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(32.dp))
            Spacer(Modifier.width(16.dp))
            Column {
                Text(title, style = MaterialTheme.typography.titleMedium)
                Spacer(Modifier.height(4.dp))
                Text(subtitle, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}
