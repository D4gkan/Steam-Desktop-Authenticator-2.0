# Keep model classes intact - they're (de)serialized by field name via kotlinx.serialization
# and must round-trip exactly for .maFile / manifest.json compatibility with the desktop app.
-keep class com.sda.mobile.model.** { *; }
-keepclassmembers class com.sda.mobile.model.** { *; }

# kotlinx.serialization
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.AnnotationsKt
-keepclassmembers class kotlinx.serialization.json.** {
    *** Companion;
}
-keepclasseswithmembers class **$$serializer {
    *** INSTANCE;
}
