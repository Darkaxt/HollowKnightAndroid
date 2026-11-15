package io.github.darkaxt.dualsouls.lab

import android.app.Activity
import android.os.Bundle
import android.os.Process
import android.widget.Button
import android.widget.TextView
import dev.silksong.launcher.profiles.GameProfiles
import io.github.darkaxt.dualsouls.emutest.R

class LabGameActivity : Activity() {
    // Lab-only stand-in for the game-side skin choice: death picks, respawn applies.
    private val skins = listOf("blue", "red", "gold")
    private var activeSkin: String? = "blue"
    private var pendingSkin: String? = null
    private lateinit var lifecycle: TextView
    private lateinit var skin: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_lab_game)
        val profileId = requireNotNull(intent.getStringExtra(LabLauncherRuntime.PROFILE_ID_EXTRA))
        val profile = GameProfiles.require(profileId)
        val generation = requireNotNull(intent.getStringExtra(LabLauncherRuntime.GENERATION_ID_EXTRA))
        findViewById<TextView>(R.id.txt_lab_profile).text = "${profile.displayName} (${profile.id})"
        findViewById<TextView>(R.id.txt_lab_generation).text = generation
        findViewById<TextView>(R.id.txt_lab_features).text =
            "mods=none · eligible skins=blue,red,gold"
        lifecycle = findViewById(R.id.txt_lab_lifecycle)
        skin = findViewById(R.id.txt_lab_skin)
        findViewById<Button>(R.id.btn_lab_death).setOnClickListener {
            pendingSkin = skins.filter { it != activeSkin }.randomOrNull()
            render("DEAD")
        }
        findViewById<Button>(R.id.btn_lab_respawn).setOnClickListener {
            pendingSkin?.let { activeSkin = it }
            pendingSkin = null
            render("STABLE_RESPAWN")
        }
        findViewById<Button>(R.id.btn_lab_exit).setOnClickListener {
            render("EXIT")
            LabLauncherRuntime.recordCleanExit(this, profile.id, generation)
            finishAndRemoveTask()
            Process.killProcess(Process.myPid())
        }
        render("ALIVE")
    }

    private fun render(event: String) {
        lifecycle.text = event
        skin.text = "active=${activeSkin ?: "none"} · pending=${pendingSkin ?: "none"}"
    }
}
