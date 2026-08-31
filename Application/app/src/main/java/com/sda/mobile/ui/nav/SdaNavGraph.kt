package com.sda.mobile.ui.nav

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import androidx.navigation.navArgument
import com.sda.mobile.ui.screens.AccountListScreen
import com.sda.mobile.ui.screens.AddAccountScreen
import com.sda.mobile.ui.screens.ConfirmationsScreen
import com.sda.mobile.ui.screens.LoginScreen
import com.sda.mobile.ui.screens.PasskeyUnlockScreen
import com.sda.mobile.ui.screens.QrExportScreen
import com.sda.mobile.ui.screens.SettingsScreen
import com.sda.mobile.ui.viewmodel.AppViewModel
import com.sda.mobile.ui.viewmodel.LoginPurpose

object Routes {
    const val ACCOUNT_LIST = "account_list"
    const val PASSKEY_UNLOCK = "passkey_unlock"
    const val ADD_ACCOUNT = "add_account"
    const val CONFIRMATIONS = "confirmations"
    const val SETTINGS = "settings"
    const val QR_EXPORT = "qr_export/{steamId}"
    const val LOGIN = "login/{purpose}/{steamId}"

    fun qrExport(steamId: Long) = "qr_export/$steamId"
    fun login(purpose: LoginPurpose, steamId: Long = 0L) = "login/${purpose.name}/$steamId"
}

@Composable
fun SdaNavGraph(navController: NavHostController = rememberNavController()) {
    // One AppViewModel for the whole nav graph. It's created here - scoped to whatever hosts
    // SdaNavGraph (the Activity) rather than to any single destination - and handed explicitly
    // to every screen below. Each screen previously requested its own default `viewModel()`,
    // which Navigation-Compose scopes to that destination's own NavBackStackEntry: every screen
    // silently got a *different* AppViewModel instance (and therefore a different in-memory
    // copy of the account list) instead of sharing one. That's why an account added from
    // AddAccountScreen didn't show up on AccountListScreen until the app - and its ViewModels -
    // were fully recreated on restart.
    val appViewModel: AppViewModel = viewModel()
    val navBackStackEntry by navController.currentBackStackEntryAsState()
    val currentRoute = navBackStackEntry?.destination?.route ?: Routes.ACCOUNT_LIST
    val showBottomBar = currentRoute == Routes.ACCOUNT_LIST || currentRoute == Routes.CONFIRMATIONS

    Scaffold(
        modifier = Modifier.fillMaxSize(),
        bottomBar = {
            if (showBottomBar) {
                NavigationBar {
                    NavigationBarItem(
                        selected = currentRoute == Routes.ACCOUNT_LIST,
                        onClick = {
                            if (currentRoute != Routes.ACCOUNT_LIST) {
                                navController.navigate(Routes.ACCOUNT_LIST) {
                                    popUpTo(Routes.ACCOUNT_LIST) { inclusive = true }
                                    launchSingleTop = true
                                }
                            }
                        },
                        icon = { Icon(Icons.Default.AccountCircle, contentDescription = null) },
                        label = { Text("Accounts") }
                    )
                    NavigationBarItem(
                        selected = currentRoute == Routes.CONFIRMATIONS,
                        onClick = {
                            if (currentRoute != Routes.CONFIRMATIONS) {
                                navController.navigate(Routes.CONFIRMATIONS) {
                                    popUpTo(Routes.ACCOUNT_LIST) { inclusive = false }
                                    launchSingleTop = true
                                }
                            }
                        },
                        icon = { Icon(Icons.Default.CheckCircle, contentDescription = null) },
                        label = { Text("Confirmations") }
                    )
                }
            }
        }
    ) { innerPadding ->
        NavHost(
            navController = navController,
            startDestination = Routes.ACCOUNT_LIST,
            modifier = Modifier.fillMaxSize()
        ) {
            composable(Routes.ACCOUNT_LIST) {
                AccountListScreen(
                    viewModel = appViewModel,
                    onUnlockRequired = { navController.navigate(Routes.PASSKEY_UNLOCK) },
                    onAddAccount = { navController.navigate(Routes.ADD_ACCOUNT) },
                    onOpenConfirmations = { navController.navigate(Routes.CONFIRMATIONS) },
                    onOpenSettings = { navController.navigate(Routes.SETTINGS) },
                    onExportAccount = { steamId -> navController.navigate(Routes.qrExport(steamId)) },
                    onRefreshSession = { steamId -> navController.navigate(Routes.login(LoginPurpose.REFRESH, steamId)) }
                )
            }

            composable(Routes.PASSKEY_UNLOCK) {
                PasskeyUnlockScreen(viewModel = appViewModel, onUnlocked = { navController.popBackStack() })
            }

            composable(Routes.ADD_ACCOUNT) {
                AddAccountScreen(
                    viewModel = appViewModel,
                    onDone = { navController.popBackStack(Routes.ACCOUNT_LIST, inclusive = false) },
                    onStartLogin = { navController.navigate(Routes.login(LoginPurpose.INITIAL)) }
                )
            }

            composable(
                route = Routes.LOGIN,
                arguments = listOf(
                    navArgument("purpose") { type = NavType.StringType },
                    navArgument("steamId") { type = NavType.LongType; defaultValue = 0L }
                )
            ) { backStackEntry ->
                val purpose = LoginPurpose.valueOf(backStackEntry.arguments?.getString("purpose") ?: "INITIAL")
                val steamId = backStackEntry.arguments?.getLong("steamId") ?: 0L
                LoginScreen(
                    purpose = purpose,
                    steamId64 = steamId,
                    appViewModel = appViewModel,
                    onFinished = { navController.popBackStack(Routes.ACCOUNT_LIST, inclusive = false) },
                    onCancel = { navController.popBackStack() }
                )
            }

            composable(Routes.CONFIRMATIONS) {
                ConfirmationsScreen(appViewModel = appViewModel, onBack = { navController.popBackStack() })
            }

            composable(Routes.SETTINGS) {
                SettingsScreen(viewModel = appViewModel, onBack = { navController.popBackStack() })
            }

            composable(
                route = Routes.QR_EXPORT,
                arguments = listOf(navArgument("steamId") { type = NavType.LongType })
            ) { backStackEntry ->
                val steamId = backStackEntry.arguments?.getLong("steamId") ?: 0L
                QrExportScreen(steamId64 = steamId, viewModel = appViewModel, onBack = { navController.popBackStack() })
            }
        }
    }
}
