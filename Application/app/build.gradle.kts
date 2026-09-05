plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
    id("org.jetbrains.kotlin.plugin.serialization")
}

android {
    namespace = "com.sda.mobile"
    compileSdk = 35

    signingConfigs {
        create("release") {
            storeFile = file("../sda-release-key.jks")
            storePassword = "sda2026!"
            keyAlias = "sda-release"
            keyPassword = "sda2026!"
        }
    }

    defaultConfig {
        applicationId = "com.sda.mobile"
        minSdk = 26
        targetSdk = 35
        versionCode = 5
        versionName = "2.0.5"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            signingConfig = signingConfigs.getByName("release")
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
        debug {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

dependencies {
    // --- Core / Compose ---
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation(platform("androidx.compose:compose-bom:2024.12.01"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.navigation:navigation-compose:2.8.4")
    debugImplementation("androidx.compose.ui:ui-tooling")

    // --- Networking ---
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // Coil: async image loading + on-disk/memory caching for account avatars (v2.0.3).
    implementation("io.coil-kt:coil-compose:2.7.0")

    // --- JSON (field names mirror the desktop app's Newtonsoft.Json models exactly,
    //     see model/*.kt - this keeps .maFile / manifest.json interchangeable with desktop) ---
    implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.7.3")

    // --- Coroutines ---
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // --- Secure local storage (Android Keystore-backed) for the saved-password /
    //     automatic-relogin feature, equivalent to Windows Credential Manager / macOS
    //     Keychain / Linux Secret Service on desktop ---
    implementation("androidx.security:security-crypto:1.1.0-alpha06")
    // Required by com.google.crypto.tink at release/minified build time.
    implementation("com.google.errorprone:error_prone_annotations:2.36.0")

    // --- Biometric unlock for the local encryption passkey ---
    implementation("androidx.biometric:biometric-ktx:1.2.0-alpha05")

    // --- QR scanning (importing a maFile transferred from the desktop app's QR export) ---
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")

    // --- DataStore for small bits of app-level settings (poll interval, theme, etc.) ---
    implementation("androidx.datastore:datastore-preferences:1.1.1")

    testImplementation("junit:junit:4.13.2")
}
