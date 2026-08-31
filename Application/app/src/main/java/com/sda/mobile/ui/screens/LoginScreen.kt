package com.sda.mobile.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.sda.mobile.model.SteamGuardAccount
import com.sda.mobile.ui.viewmodel.AppViewModel
import com.sda.mobile.ui.viewmodel.LoginPhase
import com.sda.mobile.ui.viewmodel.LoginPurpose
import com.sda.mobile.ui.viewmodel.LoginViewModel
import com.sda.mobile.model.AuthGuardType

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LoginScreen(
    purpose: LoginPurpose,
    steamId64: Long,
    onFinished: () -> Unit,
    onCancel: () -> Unit,
    appViewModel: AppViewModel
) {
    val appState by appViewModel.state.collectAsState()
    val existingAccount = remember(appState.accounts) { appState.accounts.firstOrNull { it.account.steamId64 == steamId64 }?.account }
    val savedPassword = remember(existingAccount?.steamId64) { existingAccount?.steamId64?.let { appViewModel.getSavedPassword(it) } }

    val application = androidx.compose.ui.platform.LocalContext.current.applicationContext as android.app.Application
    val viewModel: LoginViewModel = androidx.lifecycle.viewmodel.compose.viewModel(
        factory = LoginViewModel.Factory(application, purpose, existingAccount)
    )
    val state by viewModel.state.collectAsState()

    // Handle terminal states: REFRESH/IMPORT have nothing more to show, so hand off and pop
    // immediately. INITIAL instead saves the new account right away (so it's never lost) but
    // stays on screen showing the revocation code until the user explicitly confirms they've
    // saved it - see RevocationCodeStep's Done button.
    var initialAccountSaved by remember { mutableStateOf(false) }
    LaunchedEffect(state.phase) {
        when {
            state.phase == LoginPhase.DONE && purpose == LoginPurpose.REFRESH -> {
                state.resultSession?.let { session ->
                    appViewModel.refreshSession(steamId64, session) { onFinished() }
                } ?: onFinished()
            }
            state.phase == LoginPhase.DONE && purpose == LoginPurpose.IMPORT -> onFinished()
            state.phase == LoginPhase.DONE && purpose == LoginPurpose.INITIAL && !initialAccountSaved -> {
                state.resultAccount?.let { appViewModel.addLinkedAccount(it) { } }
                initialAccountSaved = true
            }
        }
    }

    Scaffold(topBar = {
        TopAppBar(title = {
            Text(
                when (purpose) {
                    LoginPurpose.INITIAL -> "Add authenticator"
                    LoginPurpose.REFRESH -> "Refresh session"
                    LoginPurpose.IMPORT -> "Log in"
                }
            )
        })
    }) { padding ->
        Box(Modifier.fillMaxSize().padding(padding).padding(24.dp)) {
            when (state.phase) {
                LoginPhase.CREDENTIALS -> CredentialsStep(
                    purpose = purpose,
                    prefillUsername = existingAccount?.accountName,
                    prefillPassword = savedPassword,
                    busy = state.busy,
                    error = state.error,
                    onSubmit = { u, p, savePassword ->
                        if (savePassword && steamId64 != 0L) {
                            appViewModel.savePasswordForAccount(steamId64, p)
                        }
                        viewModel.submitCredentials(u, p)
                    },
                    onCancel = onCancel
                )
                LoginPhase.GUARD_CODE -> GuardCodeStep(
                    guardType = state.guardType,
                    message = state.guardMessage,
                    busy = state.busy,
                    error = state.error,
                    autoCode = remember(state.guardType) { viewModel.tryAutoGenerateDeviceCode() },
                    onSubmit = { viewModel.submitGuardCode(it) }
                )
                LoginPhase.LINK_CONFIRM -> ConfirmLinkStep(busy = state.busy, onConfirm = { viewModel.confirmProceedWithLink() }, onCancel = onCancel)
                LoginPhase.PHONE_NUMBER -> PhoneNumberStep(busy = state.busy, error = state.error, onSubmit = { phone, cc -> viewModel.submitPhoneNumber(phone, cc) })
                LoginPhase.PHONE_EMAIL_WAIT -> PhoneEmailWaitStep(email = state.confirmationEmail, busy = state.busy, error = state.error, onCheck = { viewModel.checkEmailConfirmed() })
                LoginPhase.PHONE_SMS -> CodeEntryStep(
                    title = "Enter the SMS code",
                    subtitle = "Steam texted a code to your new phone number.",
                    busy = state.busy, error = state.error, onSubmit = { viewModel.submitPhoneSmsCode(it) }
                )
                LoginPhase.LINK_SMS -> CodeEntryStep(
                    title = "Enter the SMS code",
                    subtitle = "Steam texted a code to finish linking this authenticator. IMPORTANT: once you submit this, write down the revocation code shown on the next screen and keep it somewhere safe.",
                    busy = state.busy, error = state.error, onSubmit = { viewModel.submitFinalizeSmsCode(it) }
                )
                LoginPhase.DONE -> RevocationCodeStep(purpose = purpose, account = state.resultAccount, onDone = onFinished)
                LoginPhase.ERROR -> ErrorStep(message = state.error ?: "Something went wrong.", onBack = onCancel)
            }
        }
    }
}

@Composable
private fun CredentialsStep(
    purpose: LoginPurpose,
    prefillUsername: String?,
    prefillPassword: String?,
    busy: Boolean,
    error: String?,
    onSubmit: (String, String, Boolean) -> Unit,
    onCancel: () -> Unit
) {
    var username by remember(prefillUsername) { mutableStateOf(prefillUsername ?: "") }
    var password by remember(prefillPassword) { mutableStateOf(prefillPassword ?: "") }
    var savePassword by remember(prefillPassword) { mutableStateOf(prefillPassword != null) }

    Column {
        Text(
            when (purpose) {
                LoginPurpose.REFRESH -> "Your Steam session expired. Log in again for trade/market confirmations to keep working."
                LoginPurpose.IMPORT -> "Log in to refresh the session for this imported account."
                LoginPurpose.INITIAL -> "Log in to link this phone as your Steam Guard authenticator."
            },
            style = MaterialTheme.typography.bodyLarge
        )
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = username, onValueChange = { username = it },
            label = { Text("Steam username") }, singleLine = true,
            modifier = Modifier.fillMaxWidth()
        )
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = password, onValueChange = { password = it },
            label = { Text("Password") }, singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            modifier = Modifier.fillMaxWidth()
        )
        error?.let {
            Spacer(Modifier.height(8.dp))
            Text(it, color = MaterialTheme.colorScheme.error)
        }
        Spacer(Modifier.height(8.dp))
        Row(verticalAlignment = Alignment.CenterVertically) {
            androidx.compose.material3.Checkbox(
                checked = savePassword,
                onCheckedChange = { savePassword = it }
            )
            Text("Save password for automatic refresh")
        }
        Spacer(Modifier.height(20.dp))
        Button(
            onClick = { onSubmit(username, password, savePassword) },
            enabled = !busy && username.isNotBlank() && password.isNotBlank(),
            modifier = Modifier.fillMaxWidth()
        ) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Log in")
        }
        Spacer(Modifier.height(8.dp))
        TextButton(onClick = onCancel, modifier = Modifier.fillMaxWidth()) { Text("Cancel") }
    }
}

@Composable
private fun GuardCodeStep(
    guardType: AuthGuardType?,
    message: String?,
    busy: Boolean,
    error: String?,
    autoCode: String?,
    onSubmit: (String) -> Unit
) {
    var code by remember(autoCode) { mutableStateOf(autoCode ?: "") }

    LaunchedEffect(autoCode) {
        // We already hold this account's shared_secret (a Refresh/Import login) - submit the
        // locally-generated TOTP code automatically, matching AvaloniaAuthenticator on desktop
        // instead of making the user type a code they never see anywhere.
        if (autoCode != null) onSubmit(autoCode)
    }

    if (autoCode != null) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        return
    }

    Column {
        Text(
            when (guardType) {
                AuthGuardType.EMAIL_CODE -> "Enter the Steam Guard code sent to your email."
                AuthGuardType.DEVICE_CODE -> "Enter the Steam Guard Mobile code from your currently-linked authenticator."
                AuthGuardType.DEVICE_CONFIRMATION -> "Approve this login from your currently-linked Steam Mobile app, then continue."
                else -> "Enter your Steam Guard code."
            },
            style = MaterialTheme.typography.bodyLarge
        )
        message?.let { Spacer(Modifier.height(4.dp)); Text(it, style = MaterialTheme.typography.bodyMedium) }
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(
            value = code, onValueChange = { code = it.uppercase() },
            label = { Text("Code") }, singleLine = true,
            keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = KeyboardType.Text),
            modifier = Modifier.fillMaxWidth()
        )
        error?.let { Spacer(Modifier.height(8.dp)); Text(it, color = MaterialTheme.colorScheme.error) }
        Spacer(Modifier.height(20.dp))
        Button(onClick = { onSubmit(code) }, enabled = !busy && code.isNotBlank(), modifier = Modifier.fillMaxWidth()) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Continue")
        }
    }
}

@Composable
private fun ConfirmLinkStep(busy: Boolean, onConfirm: () -> Unit, onCancel: () -> Unit) {
    Column {
        Text("Steam login succeeded.", style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text("Continue adding SDA Mobile as this account's Steam Guard authenticator? This will replace any other authenticator method currently in use.", style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(20.dp))
        Button(onClick = onConfirm, enabled = !busy, modifier = Modifier.fillMaxWidth()) { Text("Continue") }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onCancel, modifier = Modifier.fillMaxWidth()) { Text("Abort") }
    }
}

@Composable
private fun PhoneNumberStep(busy: Boolean, error: String?, onSubmit: (String, String?) -> Unit) {
    var phone by remember { mutableStateOf("") }
    var country by remember { mutableStateOf("") }
    Column {
        Text("This account has no phone number. Steam requires one before adding an authenticator.", style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(value = phone, onValueChange = { phone = it }, label = { Text("Phone number (with country code, e.g. +15551234567)") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(value = country, onValueChange = { country = it.uppercase() }, label = { Text("Country code (optional, e.g. US)") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        error?.let { Spacer(Modifier.height(8.dp)); Text(it, color = MaterialTheme.colorScheme.error) }
        Spacer(Modifier.height(20.dp))
        Button(onClick = { onSubmit(phone, country.ifBlank { null }) }, enabled = !busy && phone.isNotBlank(), modifier = Modifier.fillMaxWidth()) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Continue")
        }
    }
}

@Composable
private fun PhoneEmailWaitStep(email: String?, busy: Boolean, error: String?, onCheck: () -> Unit) {
    Column {
        Text("Check your email${if (email != null) " ($email)" else ""} and click the confirmation link, then come back here.", style = MaterialTheme.typography.bodyLarge)
        error?.let { Spacer(Modifier.height(8.dp)); Text(it, color = MaterialTheme.colorScheme.error) }
        Spacer(Modifier.height(20.dp))
        Button(onClick = onCheck, enabled = !busy, modifier = Modifier.fillMaxWidth()) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("I clicked the link")
        }
    }
}

@Composable
private fun CodeEntryStep(title: String, subtitle: String, busy: Boolean, error: String?, onSubmit: (String) -> Unit) {
    var code by remember { mutableStateOf("") }
    Column {
        Text(title, style = MaterialTheme.typography.titleMedium)
        Spacer(Modifier.height(8.dp))
        Text(subtitle, style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(20.dp))
        OutlinedTextField(value = code, onValueChange = { code = it }, label = { Text("Code") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        error?.let { Spacer(Modifier.height(8.dp)); Text(it, color = MaterialTheme.colorScheme.error) }
        Spacer(Modifier.height(20.dp))
        Button(onClick = { onSubmit(code) }, enabled = !busy && code.isNotBlank(), modifier = Modifier.fillMaxWidth()) {
            if (busy) CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp) else Text("Submit")
        }
    }
}

@Composable
private fun RevocationCodeStep(purpose: LoginPurpose, account: SteamGuardAccount?, onDone: () -> Unit) {
    Column {
        if (purpose == LoginPurpose.INITIAL && account != null) {
            Text("Authenticator linked!", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.primary)
            Spacer(Modifier.height(16.dp))
            Text("Your revocation code (write this down somewhere safe - you'll need it if you ever lose this phone and have to remove Steam Guard):", style = MaterialTheme.typography.bodyLarge)
            Spacer(Modifier.height(12.dp))
            Text(
                account.revocationCode ?: "(not provided)",
                style = MaterialTheme.typography.titleLarge,
                fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace
            )
            Spacer(Modifier.height(24.dp))
            Button(onClick = onDone, modifier = Modifier.fillMaxWidth()) { Text("I've saved it") }
        } else {
            Text("Done.", style = MaterialTheme.typography.titleLarge)
        }
    }
}

@Composable
private fun ErrorStep(message: String, onBack: () -> Unit) {
    Column {
        Text(message, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodyLarge)
        Spacer(Modifier.height(20.dp))
        Button(onClick = onBack, modifier = Modifier.fillMaxWidth()) { Text("Back") }
    }
}
