package com.cts.terminal_simulation

import android.app.Application
import io.flutter.embedding.engine.FlutterEngine

class CtsApplication : Application() {
    private var cachedEngine: FlutterEngine? = null
    lateinit var runtime: CtsRuntimeBridge
        private set
    fun engine(): FlutterEngine {
        cachedEngine?.let { return it }
        val engine = FlutterEngine(this)
        cachedEngine = engine
        runtime = CtsRuntimeBridge(this, engine.dartExecutor.binaryMessenger)
        return engine
    }
}
