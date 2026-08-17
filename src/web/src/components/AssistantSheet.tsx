import { useEffect, useMemo, useRef, useState } from 'react'
import { assistantApi } from '../api/services'
import { ApiError } from '../api/client'
import type { AssistantAction, AssistantChatResult, AssistantTurn } from '../api/types'
import { notifyLogged } from '../lib/logged'
import { isSpeechRecognitionSupported, startDictation, type Dictation } from '../lib/speech'
import { Sheet } from './Sheet'

/** One line of the visible exchange. The provider-shaped transcript is kept separately and untouched. */
interface Line {
  who: 'you' | 'assistant'
  text: string
}

const EXAMPLES = [
  'I ate a quarter pounder with fries from McDonalds',
  'I ran 5k this morning',
  'Two eggs and toast for breakfast',
]

/**
 * The AI assistant, as a bottom sheet (DESIGN.md §2.1/§3.2) hung off the LOG action.
 *
 * Typing always works. Voice is an enhancement layered on top via the browser's built-in
 * `SpeechRecognition`: when the browser has no recognizer (Firefox, and anything non-browser) the mic
 * simply isn't rendered and a one-line note explains why — the text path is unaffected either way.
 *
 * When the server comes back needing confirmation the sheet shows the question with Yes/No shortcuts; the
 * answer is posted back with the conversation and the pending id, which is what resumes the server's loop.
 */
export function AssistantSheet({ onClose }: { onClose: () => void }) {
  // Support is a fixed property of the browser — read once, at mount, without an effect (an effect that
  // called setState here would re-render for nothing).
  const voiceSupported = useMemo(() => isSpeechRecognitionSupported(), [])

  const [text, setText] = useState('')
  const [lines, setLines] = useState<Line[]>([])
  const [actions, setActions] = useState<AssistantAction[]>([])
  const [conversation, setConversation] = useState<AssistantTurn[]>([])
  const [pendingToolUseId, setPendingToolUseId] = useState<string | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const [listening, setListening] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)

  const dictationRef = useRef<Dictation | null>(null)
  const transcriptRef = useRef<HTMLDivElement>(null)

  // Stop any in-flight dictation if the sheet is closed mid-sentence. No state is set here, so this cannot
  // cascade renders.
  useEffect(() => () => { dictationRef.current?.abort() }, [])

  // Keep the newest line in view. Assigning scrollTop is a DOM write, not a state update.
  useEffect(() => {
    const el = transcriptRef.current
    if (el) el.scrollTop = el.scrollHeight
  }, [lines, busy])

  function stopListening() {
    dictationRef.current?.stop()
    dictationRef.current = null
    setListening(false)
  }

  function toggleMic() {
    if (listening) {
      stopListening()
      return
    }
    setError(null)
    const session = startDictation({
      // Interim results stream into the box so the user can see it working, and can edit before sending.
      onInterim: (heard) => setText(heard),
      onFinal: (heard) => setText(heard),
      onError: (message) => setError(message),
      onEnd: () => { dictationRef.current = null; setListening(false) },
    })
    if (!session) {
      setError('Voice input could not start. Type it instead.')
      return
    }
    dictationRef.current = session
    setListening(true)
  }

  async function send(message: string) {
    const trimmed = message.trim()
    if (!trimmed || busy) return

    if (listening) stopListening()
    setError(null)
    setBusy(true)
    setText('')
    setLines((prev) => [...prev, { who: 'you', text: trimmed }])

    try {
      const result: AssistantChatResult = await assistantApi.chat({
        message: trimmed,
        conversation: conversation.length > 0 ? conversation : undefined,
        pendingToolUseId,
      })

      setConversation(result.conversation)
      setPendingToolUseId(result.status === 'NeedsConfirmation' ? result.pendingToolUseId : undefined)
      setUnavailable(result.status === 'Unavailable')
      setLines((prev) => [...prev, { who: 'assistant', text: result.reply }])

      if (result.actions.length > 0) {
        setActions((prev) => [...prev, ...result.actions])
        // Today is mounted behind this sheet; tell it to re-read rather than showing stale totals.
        notifyLogged()
      }
    } catch (e) {
      const message = e instanceof ApiError
        ? e.message
        : e instanceof Error ? e.message : 'The assistant could not be reached.'
      setError(message)
    } finally {
      setBusy(false)
    }
  }

  const awaitingAnswer = pendingToolUseId !== undefined
  const started = lines.length > 0

  return (
    <Sheet title="Assistant" onClose={onClose}>
      <div className="stack">
        {!started && (
          <div className="stack" style={{ gap: '0.5rem' }}>
            <p className="muted small" style={{ margin: 0 }}>
              Say or type what you ate or did, in your own words. I'll find it in your library, build what's
              missing, and log it — checking with you before I create anything.
            </p>
            <div className="assistant-examples">
              {EXAMPLES.map((example) => (
                <button
                  key={example}
                  type="button"
                  className="finder-tag"
                  disabled={busy}
                  onClick={() => void send(example)}
                >
                  {example}
                </button>
              ))}
            </div>
          </div>
        )}

        {started && (
          <div className="assistant-transcript" ref={transcriptRef}>
            {lines.map((line, i) => (
              <div key={i} className={`assistant-line ${line.who}`}>
                <span className="stat-label">{line.who === 'you' ? 'You' : 'Assistant'}</span>
                <p>{line.text}</p>
              </div>
            ))}
            {busy && <p className="muted small" style={{ margin: 0 }}>Working on it…</p>}
          </div>
        )}

        {actions.length > 0 && (
          <div className="card stack" style={{ gap: '0.4rem', padding: '0.7rem' }}>
            <span className="stat-label">Recorded</span>
            {actions.map((action, i) => (
              <span key={i} className="small">{action.summary}</span>
            ))}
          </div>
        )}

        {error && <div className="notice error">{error}</div>}

        {unavailable && (
          <div className="notice info">
            Nothing was logged. Use the Fuel and Burn forms from the Log menu instead.
          </div>
        )}

        {awaitingAnswer && !busy && (
          <div className="row" style={{ gap: '0.5rem' }}>
            <button type="button" className="btn btn-primary btn-sm" onClick={() => void send('Yes, go ahead.')}>
              Yes, go ahead
            </button>
            <button type="button" className="btn btn-sm" onClick={() => void send('No, don’t do that.')}>
              No
            </button>
          </div>
        )}

        <div className="assistant-compose">
          <input
            className="input"
            value={text}
            disabled={busy}
            placeholder={awaitingAnswer ? 'Answer, or add a correction…' : 'What did you eat or do?'}
            aria-label={awaitingAnswer ? 'Your answer' : 'Describe what you ate or did'}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void send(text) } }}
          />
          {voiceSupported && (
            <button
              type="button"
              className={`btn btn-sm${listening ? ' btn-burn' : ''}`}
              disabled={busy}
              aria-pressed={listening}
              aria-label={listening ? 'Stop listening' : 'Dictate with your microphone'}
              onClick={toggleMic}
            >
              {listening ? 'Listening…' : 'Speak'}
            </button>
          )}
          <button
            type="button"
            className="btn btn-primary btn-sm"
            disabled={busy || !text.trim()}
            onClick={() => void send(text)}
          >
            Send
          </button>
        </div>

        {!voiceSupported && (
          <p className="muted small" style={{ margin: 0 }}>
            This browser can't transcribe speech (Firefox, for example), so the microphone isn't available.
            Typing works exactly the same.
          </p>
        )}
      </div>
    </Sheet>
  )
}
