package com.sda.mobile.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

private val DarkColors = darkColorScheme(
    primary = SteamBlue,
    onPrimary = Color(0xFF0E1116),
    secondary = SteamBlueDark,
    background = SdaBackground,
    onBackground = Color(0xFFEAF2FF),
    surface = SdaSurface,
    surfaceVariant = SdaSurfaceVariant,
    onSurface = Color(0xFFEAF2FF),
    onSurfaceVariant = SdaOnSurfaceMuted,
    error = SdaError,
    onError = Color(0xFF0E1116)
)

private val LightColors = lightColorScheme(
    primary = SteamBlueDark,
    secondary = SteamBlue,
    error = SdaError
)

private val AppTypography = Typography(
    titleLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 22.sp),
    titleMedium = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 17.sp),
    bodyLarge = TextStyle(fontSize = 15.sp),
    bodyMedium = TextStyle(fontSize = 13.sp),
    labelLarge = TextStyle(fontWeight = FontWeight.Medium, fontSize = 14.sp)
)

@Composable
fun SdaMobileTheme(darkTheme: Boolean = true, content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        typography = AppTypography,
        content = content
    )
}
