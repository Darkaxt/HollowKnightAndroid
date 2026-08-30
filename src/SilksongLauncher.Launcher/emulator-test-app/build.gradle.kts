plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "io.github.darkaxt.dualsouls.emutest"
    compileSdk = 36

    defaultConfig {
        applicationId = "io.github.darkaxt.dualsouls.emutest"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1-lab"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        debug {
            isDebuggable = true
            applicationIdSuffix = ""
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    testOptions {
        unitTests.isIncludeAndroidResources = true
    }

    sourceSets {
        getByName("main") {
            res.srcDir(rootProject.file("../../tools/depot-to-apk/shell/res"))
        }
    }

    packaging {
        jniLibs.excludes += "**/arm64-v8a/**"
        resources.excludes += "META-INF/{AL2.0,LGPL2.1}"
        resources.excludes += "META-INF/versions/9/OSGI-INF/MANIFEST.MF"
    }
}

androidComponents {
    beforeVariants(selector().all()) { variant ->
        if (variant.buildType != "debug") variant.enable = false
    }
}

dependencies {
    implementation(project(":app"))

    testImplementation("junit:junit:4.13.2")
    testImplementation("org.robolectric:robolectric:4.14.1")
    testImplementation("androidx.test:core:1.6.1")

    androidTestImplementation("androidx.test.ext:junit:1.2.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test:rules:1.6.1")
    androidTestImplementation("androidx.test.espresso:espresso-core:3.6.1")
}
