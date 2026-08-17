/**
 * A thin wrapper over the browser's built-in Web Speech API (`SpeechRecognition`), used to transcribe the
 * user's voice locally before sending plain text to the API.
 *
 * Why this and not a paid speech-to-text service: recognition runs in the browser, costs nothing, and means
 * audio never leaves the device — only the resulting sentence is sent. The trade-off is patchy support
 * (Chrome/Edge/Safari yes, Firefox no), so **voice is always an enhancement, never the only way in**:
 * callers check `isSpeechRecognitionSupported()` and simply omit the mic when it is false. Typing must work
 * everywhere, unchanged.
 */

/** The slice of the SpeechRecognition interface we use. The DOM lib does not ship types for it. */
interface SpeechRecognitionLike {
  lang: string
  continuous: boolean
  interimResults: boolean
  maxAlternatives: number
  start(): void
  stop(): void
  abort(): void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onerror: ((event: { error?: string }) => void) | null
  onend: (() => void) | null
}

interface SpeechRecognitionEventLike {
  results: ArrayLike<ArrayLike<{ transcript: string }> & { isFinal: boolean }>
}

type SpeechRecognitionCtor = new () => SpeechRecognitionLike

/** Both the standard name and the still-dominant WebKit-prefixed one. */
function constructor(): SpeechRecognitionCtor | null {
  if (typeof window === 'undefined') return null
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionCtor
    webkitSpeechRecognition?: SpeechRecognitionCtor
  }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null
}

/** Whether this browser can transcribe speech at all. False on Firefox, and in non-browser contexts. */
export function isSpeechRecognitionSupported(): boolean {
  return constructor() !== null
}

/** Callbacks for a dictation session. `onFinal` fires once, with the complete sentence. */
export interface DictationHandlers {
  /** Best-guess text so far, updated as the user speaks. Use it to show live feedback. */
  onInterim?: (text: string) => void
  /** The finished transcript. */
  onFinal: (text: string) => void
  /** A recognition error (no microphone permission, no speech heard, network recognizer down…). */
  onError?: (message: string) => void
  /** Always fires when the session ends, however it ended — use it to reset "listening" state. */
  onEnd?: () => void
}

/** A running dictation session; call `stop()` to finish early or `abort()` to discard it. */
export interface Dictation {
  stop: () => void
  abort: () => void
}

/** Human-readable text for the error codes the API reports. */
function describe(code: string | undefined): string {
  switch (code) {
    case 'not-allowed':
    case 'service-not-allowed':
      return 'Microphone access was blocked. Allow it in your browser, or type instead.'
    case 'no-speech':
      return "I didn't catch that. Try again, or type it instead."
    case 'audio-capture':
      return 'No microphone was found. Type it instead.'
    case 'network':
      return 'The speech recognizer is unreachable. Type it instead.'
    case 'aborted':
      return '' // user-initiated; nothing worth showing.
    default:
      return 'Voice input failed. Type it instead.'
  }
}

/**
 * Starts a one-shot dictation. Returns null when the browser has no recognizer (callers should not have
 * offered the mic in that case) or when starting throws.
 */
export function startDictation(handlers: DictationHandlers): Dictation | null {
  const Ctor = constructor()
  if (!Ctor) return null

  let recognition: SpeechRecognitionLike
  try {
    recognition = new Ctor()
  } catch {
    return null
  }

  recognition.lang = typeof navigator !== 'undefined' && navigator.language ? navigator.language : 'en-US'
  // One utterance, with interim results so the field fills in as the user speaks.
  recognition.continuous = false
  recognition.interimResults = true
  recognition.maxAlternatives = 1

  recognition.onresult = (event) => {
    let interim = ''
    let final = ''
    for (let i = 0; i < event.results.length; i++) {
      const result = event.results[i]
      const text = result[0]?.transcript ?? ''
      if (result.isFinal) final += text
      else interim += text
    }
    if (final.trim()) handlers.onFinal(final.trim())
    else if (interim.trim()) handlers.onInterim?.(interim.trim())
  }

  recognition.onerror = (event) => {
    const message = describe(event.error)
    if (message) handlers.onError?.(message)
  }

  recognition.onend = () => handlers.onEnd?.()

  try {
    recognition.start()
  } catch {
    // Starting twice throws; treat it as "not started" rather than crashing the sheet.
    return null
  }

  return {
    stop: () => { try { recognition.stop() } catch { /* already stopped */ } },
    abort: () => { try { recognition.abort() } catch { /* already stopped */ } },
  }
}
