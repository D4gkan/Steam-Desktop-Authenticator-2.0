package com.sda.mobile.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.sda.mobile.crypto.SteamGuardCodeGenerator
import com.sda.mobile.model.SteamGuardAccount
import com.sda.mobile.network.TimeAligner
import com.sda.mobile.ui.viewmodel.AppViewModel
import kotlinx.coroutines.delay

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AccountListScreen(
    onUnlockRequired: () -> Unit,
    onAddAccount: () -> Unit,
    onOpenConfirmations: () -> Unit,
    onOpenSettings: () -> Unit,
    onExportAccount: (Long) -> Unit,
    onRefreshSession: (Long) -> Unit,
    viewModel: AppViewModel
) {
    val state by viewModel.state.collectAsState()

    LaunchedEffect(state.manifest.encrypted, state.unlocked) {
        if (state.manifest.encrypted && !state.unlocked) onUnlockRequired()
    }

    // Drives per-second recomposition of every code + countdown ring, without storing codes
    // in ViewModel state (they're cheap, pure functions of time - no need to churn StateFlow).
    var nowSeconds by remember { mutableStateOf(TimeAligner.getSteamTimeCached()) }
    LaunchedEffect(Unit) {
        TimeAligner.alignIfNeeded()
        while (true) {
            nowSeconds = TimeAligner.getSteamTimeCached()
            delay(1000)
        }
    }

    var accountForActions by remember { mutableStateOf<SteamGuardAccount?>(null) }
    val sessionExpiredAccountId = state.expiredSessionAccountId
    val sessionExpiredMessage = state.expiredSessionMessage

    sessionExpiredAccountId?.let { steamId ->
        val account = state.accounts.firstOrNull { it.account.steamId64 == steamId }?.account
        if (account != null) {
            AlertDialog(
                onDismissRequest = { viewModel.clearSessionExpiredPrompt() },
                title = { Text("Session expired") },
                text = { Text(sessionExpiredMessage ?: "Your Steam session expired. Please log in again.") },
                confirmButton = {
                    TextButton(onClick = {
                        viewModel.clearSessionExpiredPrompt()
                        onRefreshSession(steamId)
                    }) { Text("Log in again") }
                },
                dismissButton = {
                    TextButton(onClick = { viewModel.clearSessionExpiredPrompt() }) { Text("Dismiss") }
                }
            )
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("SDA Mobile") },
                actions = {
                    IconButton(onClick = onOpenConfirmations) {
                        Icon(Icons.Default.Notifications, contentDescription = "Confirmations")
                    }
                    IconButton(onClick = onOpenSettings) {
                        Icon(Icons.Default.Settings, contentDescription = "Settings")
                    }
                }
            )
        },
        floatingActionButton = {
            FloatingActionButton(
                onClick = onAddAccount,
                modifier = Modifier.padding(bottom = 72.dp)
            ) {
                Icon(Icons.Default.Add, contentDescription = "Add account")
            }
        }
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when {
                state.loading -> CircularProgressIndicator(Modifier.align(Alignment.Center))
                state.accounts.isEmpty() && state.unlocked -> EmptyAccountsHint(onAddAccount, Modifier.align(Alignment.Center))
                else -> LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    items(state.accounts, key = { it.account.steamId64 }) { entry ->
                        AccountCard(
                            account = entry.account,
                            displayName = entry.meta.displayName,
                            nowSeconds = nowSeconds,
                            onLongPress = { accountForActions = entry.account }
                        )
                    }
                }
            }
        }
    }

    accountForActions?.let { account ->
        AccountActionsSheet(
            account = account,
            onDismiss = { accountForActions = null },
            onRemove = { viewModel.removeAccount(account); accountForActions = null },
            onExport = { onExportAccount(account.steamId64); accountForActions = null },
            onRefreshSession = { onRefreshSession(account.steamId64); accountForActions = null },
            onRename = { newName ->
                val meta = viewModel.state.value.accounts.firstOrNull { it.account.steamId64 == account.steamId64 }?.meta
                val updated = (meta ?: com.sda.mobile.model.AccountMeta(steamId = account.steamId64)).copy(
                    displayName = newName.trim().ifBlank { account.accountName ?: "Unknown account" }
                )
                viewModel.updateMeta(updated)
                accountForActions = null
            }
        )
    }
}

@Composable
private fun EmptyAccountsHint(onAddAccount: () -> Unit, modifier: Modifier = Modifier) {
    Column(modifier.padding(32.dp), horizontalAlignment = Alignment.CenterHorizontally) {
        Icon(Icons.Default.Shield, contentDescription = null, modifier = Modifier.size(48.dp), tint = MaterialTheme.colorScheme.primary)
        Spacer(Modifier.height(16.dp))
        Text("No accounts yet", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(
            "Import an existing .maFile, scan a QR code from the desktop app, or log in to add SDA Mobile as a new authenticator.",
            style = MaterialTheme.typography.bodyMedium,
            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Spacer(Modifier.height(20.dp))
        Button(onClick = onAddAccount) { Text("Add an account") }
    }
}

@Composable
private fun AccountCard(account: SteamGuardAccount, displayName: String?, nowSeconds: Long, onLongPress: () -> Unit) {
    val clipboard = LocalClipboardManager.current
    val code = remember(account.sharedSecret, nowSeconds / 30) {
        SteamGuardCodeGenerator.generateCode(account.sharedSecret, nowSeconds)
    }
    val secondsIntoStep = (nowSeconds % 30).toInt()
    val progress = 1f - (secondsIntoStep / 30f)

    Card(
        shape = RoundedCornerShape(16.dp),
        modifier = Modifier
            .fillMaxWidth()
            .clickable {
                clipboard.setText(AnnotatedString(code))
            }
    ) {
        Row(
            Modifier.fillMaxWidth().padding(16.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Column(Modifier.weight(1f)) {
                Text(
                    text = displayName ?: account.accountName ?: "Unknown account",
                    style = MaterialTheme.typography.titleMedium
                )
                Spacer(Modifier.height(6.dp))
                Text(
                    text = code.chunked(1).joinToString(" ").ifBlank { "-----" },
                    style = MaterialTheme.typography.titleLarge,
                    fontFamily = FontFamily.Monospace,
                    fontWeight = FontWeight.Bold,
                    fontSize = 26.sp,
                    color = MaterialTheme.colorScheme.primary
                )
                if (!account.fullyEnrolled) {
                    Spacer(Modifier.height(4.dp))
                    Text("Not fully enrolled", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.error)
                }
            }

            Box(contentAlignment = Alignment.Center, modifier = Modifier.size(40.dp)) {
                CircularProgressIndicator(
                    progress = { progress },
                    modifier = Modifier.fillMaxSize(),
                    strokeWidth = 3.dp,
                    color = if (secondsIntoStep < 25) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error
                )
                Text("${30 - secondsIntoStep}", fontSize = 11.sp)
            }

            IconButton(onClick = onLongPress) {
                Icon(Icons.Default.MoreVert, contentDescription = "Account actions")
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AccountActionsSheet(
    account: SteamGuardAccount,
    onDismiss: () -> Unit,
    onRemove: () -> Unit,
    onExport: () -> Unit,
    onRefreshSession: () -> Unit,
    onRename: (String) -> Unit
) {
    var confirmingRemove by remember { mutableStateOf(false) }
    var editingName by remember { mutableStateOf(false) }
    val currentDisplayName = remember(account.accountName, account.steamId64) {
        account.accountName ?: "Unknown account"
    }
    var newDisplayName by remember(account.accountName, account.steamId64) { mutableStateOf(currentDisplayName) }

    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(Modifier.padding(bottom = 24.dp)) {
            Text(
                account.accountName ?: "Account",
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp)
            )
            ListItem(
                headlineContent = { Text("Export as QR code") },
                supportingContent = { Text("Transfer this account to another device") },
                leadingContent = { Icon(Icons.Default.QrCode, contentDescription = null) },
                modifier = Modifier.clickable(onClick = onExport)
            )
            ListItem(
                headlineContent = { Text("Refresh Steam session") },
                supportingContent = { Text("Log in again if trade/market confirmations stop working") },
                leadingContent = { Icon(Icons.Default.Refresh, contentDescription = null) },
                modifier = Modifier.clickable(onClick = onRefreshSession)
            )
            ListItem(
                headlineContent = { Text("Edit display name") },
                supportingContent = { Text("Change how this account is labeled in the app") },
                leadingContent = { Icon(Icons.Default.Edit, contentDescription = null) },
                modifier = Modifier.clickable { editingName = true }
            )
            ListItem(
                headlineContent = { Text("Remove from this device", color = MaterialTheme.colorScheme.error) },
                supportingContent = { Text("This does not remove the Steam Guard authenticator from your account") },
                leadingContent = { Icon(Icons.Default.Delete, contentDescription = null, tint = MaterialTheme.colorScheme.error) },
                modifier = Modifier.clickable { confirmingRemove = true }
            )
        }
    }

    if (editingName) {
        AlertDialog(
            onDismissRequest = { editingName = false },
            title = { Text("Edit display name") },
            text = {
                OutlinedTextField(
                    value = newDisplayName,
                    onValueChange = { newDisplayName = it },
                    label = { Text("Display name") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    onRename(newDisplayName)
                    editingName = false
                    onDismiss()
                }) { Text("Save") }
            },
            dismissButton = { TextButton(onClick = { editingName = false }) { Text("Cancel") } }
        )
    }

    if (confirmingRemove) {
        AlertDialog(
            onDismissRequest = { confirmingRemove = false },
            title = { Text("Remove this account?") },
            text = { Text("This deletes the local .maFile from this phone. Make sure you have a backup (export as QR, or the original desktop maFiles) before removing your only copy - without it you may be permanently locked out of Steam Guard for this account.") },
            confirmButton = { TextButton(onClick = onRemove) { Text("Remove", color = MaterialTheme.colorScheme.error) } },
            dismissButton = { TextButton(onClick = { confirmingRemove = false }) { Text("Cancel") } }
        )
    }
}
