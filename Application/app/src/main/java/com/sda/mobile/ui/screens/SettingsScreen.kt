package com.sda.mobile.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Divider
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.sda.mobile.BuildConfig
import com.sda.mobile.ui.viewmodel.AppViewModel

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(onBack: () -> Unit, viewModel: AppViewModel) {
    val state by viewModel.state.collectAsState()
    var showEncryptionDialog by remember { mutableStateOf(false) }
    var showDecryptDialog by remember { mutableStateOf(false) }

    Scaffold(topBar = {
        TopAppBar(
            title = { Text("Settings") },
            navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, contentDescription = "Back") } }
        )
    }) { padding ->
        Column(Modifier.padding(padding)) {
            ListItem(
                headlineContent = { Text("Encrypt local storage") },
                supportingContent = {
                    Text(
                        if (state.manifest.encrypted)
                            "Accounts are encrypted with your passkey. The passkey is never stored - if you forget it, your accounts are unrecoverable on this device."
                        else
                            "Accounts are currently stored unencrypted in this app's private storage."
                    )
                },
                trailingContent = {
                    Switch(
                        checked = state.manifest.encrypted,
                        onCheckedChange = { enabling ->
                            if (enabling) showEncryptionDialog = true else showDecryptDialog = true
                        }
                    )
                }
            )
            Divider()
            ListItem(
                headlineContent = { Text("Accounts on this device") },
                supportingContent = { Text("${state.accounts.size} account(s)") }
            )
            Divider()
            ListItem(
                headlineContent = { Text("About SDA Mobile") },
                supportingContent = { Text("Version ${BuildConfig.VERSION_NAME}. An unofficial, community-built Android companion to Steam Desktop Authenticator.") }
            )
        }
    }

    if (showEncryptionDialog) {
        SetPasskeyDialog(
            onDismiss = { showEncryptionDialog = false },
            onConfirm = { newPasskey ->
                viewModel.setEncryption(oldPasskey = viewModel.currentPasskey(), newPasskey = newPasskey) { }
                showEncryptionDialog = false
            }
        )
    }

    if (showDecryptDialog) {
        AlertDialog(
            onDismissRequest = { showDecryptDialog = false },
            title = { Text("Remove encryption?") },
            text = { Text("Your accounts will be stored unencrypted on this device from now on. Anyone with access to this phone's storage (e.g. via a backup exploit or root access) could read your Steam Guard secrets.") },
            confirmButton = {
                TextButton(onClick = {
                    viewModel.setEncryption(oldPasskey = viewModel.currentPasskey(), newPasskey = null) { }
                    showDecryptDialog = false
                }) { Text("Remove encryption", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { showDecryptDialog = false }) { Text("Cancel") } }
        )
    }
}

@Composable
private fun SetPasskeyDialog(onDismiss: () -> Unit, onConfirm: (String) -> Unit) {
    var passkey by remember { mutableStateOf("") }
    var confirmPasskey by remember { mutableStateOf("") }
    val mismatch = confirmPasskey.isNotEmpty() && passkey != confirmPasskey

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Set an encryption passkey") },
        text = {
            Column {
                Text(
                    "Choose a passkey used to encrypt your accounts on this device. There is no recovery if you forget it - write it down somewhere safe.",
                    style = MaterialTheme.typography.bodyMedium
                )
                Spacer(Modifier.height(16.dp))
                OutlinedTextField(
                    value = passkey,
                    onValueChange = { passkey = it },
                    label = { Text("Passkey") },
                    singleLine = true,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth()
                )
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = confirmPasskey,
                    onValueChange = { confirmPasskey = it },
                    label = { Text("Confirm passkey") },
                    singleLine = true,
                    isError = mismatch,
                    visualTransformation = PasswordVisualTransformation(),
                    modifier = Modifier.fillMaxWidth()
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onConfirm(passkey) },
                enabled = passkey.isNotEmpty() && passkey == confirmPasskey
            ) { Text("Set passkey") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } }
    )
}
