package com.sda.mobile.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.sda.mobile.ui.viewmodel.AppViewModel
import com.sda.mobile.ui.viewmodel.ConfirmationRow
import com.sda.mobile.ui.viewmodel.ConfirmationsViewModel
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ConfirmationsScreen(
    onBack: () -> Unit,
    appViewModel: AppViewModel,
    viewModel: ConfirmationsViewModel = viewModel()
) {
    val appState by appViewModel.state.collectAsState()
    val state by viewModel.state.collectAsState()

    // Access tokens are short-lived; mint fresh ones (via each account's refresh_token) before
    // hitting the confirmation endpoints, otherwise a merely-stale-but-recoverable token shows
    // up to the user as "session expired, please log in again".
    val coroutineScope = androidx.compose.runtime.rememberCoroutineScope()
    fun refreshConfirmations() {
        coroutineScope.launch {
            val fresh = appViewModel.ensureFreshSessions(appState.accounts)
            viewModel.refresh(fresh.map { it.account })
        }
    }

    LaunchedEffect(appState.accounts) {
        if (appState.accounts.isNotEmpty()) refreshConfirmations()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Confirmations") },
                navigationIcon = { IconButton(onClick = onBack) { Icon(Icons.Default.ArrowBack, contentDescription = "Back") } },
                actions = {
                    IconButton(onClick = { refreshConfirmations() }) {
                        Icon(Icons.Default.Refresh, contentDescription = "Refresh")
                    }
                }
            )
        }
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding)) {
            when {
                state.loading && state.rows.isEmpty() -> CircularProgressIndicator(Modifier.align(Alignment.Center))
                state.rows.isEmpty() -> Column(Modifier.align(Alignment.Center).padding(24.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(state.error ?: "No pending confirmations.", style = MaterialTheme.typography.bodyLarge)
                    Spacer(Modifier.height(12.dp))
                    OutlinedButton(onClick = { refreshConfirmations() }) { Text("Check again") }
                }
                else -> LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                    items(state.rows, key = { it.confirmation.id }) { row ->
                        ConfirmationCard(
                            row = row,
                            busy = row.confirmation.id in state.pendingIds,
                            onAccept = { viewModel.answer(row, allow = true) {} },
                            onDeny = { viewModel.answer(row, allow = false) {} }
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ConfirmationCard(row: ConfirmationRow, busy: Boolean, onAccept: () -> Unit, onDeny: () -> Unit) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(row.account.accountName ?: "", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(4.dp))
            Text(row.confirmation.headline ?: row.confirmation.typeName ?: "Confirmation", style = MaterialTheme.typography.titleMedium)
            row.confirmation.summary.forEach { line ->
                Spacer(Modifier.height(2.dp))
                Text(line, style = MaterialTheme.typography.bodyMedium)
            }
            Spacer(Modifier.height(12.dp))
            if (busy) {
                LinearProgressIndicator(Modifier.fillMaxWidth())
            } else {
                androidx.compose.foundation.layout.Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Button(onClick = onAccept, colors = ButtonDefaults.buttonColors()) {
                        Icon(Icons.Default.Check, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Approve")
                    }
                    OutlinedButton(onClick = onDeny) {
                        Icon(Icons.Default.Close, contentDescription = null, modifier = Modifier.size(18.dp))
                        Spacer(Modifier.width(6.dp))
                        Text("Deny")
                    }
                }
            }
        }
    }
}
