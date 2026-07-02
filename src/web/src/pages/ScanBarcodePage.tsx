import { useEffect, useRef, useState } from 'react'
import { BrowserMultiFormatReader, type IScannerControls } from '@zxing/browser'
import { useNavigate } from 'react-router-dom'
import { ingredientApi, ocrApi } from '../api/services'
import type { ScanPrefill } from './IngredientFormPage'

type Phase = 'scanning' | 'looking-up' | 'not-found' | 'reading-label'

/**
 * Barcode scan entry point. Uses the device camera (via ZXing) to read a UPC/EAN, then:
 *  - opens an existing library ingredient with that barcode (re-scan), or
 *  - pre-fills a new ingredient from Open Food Facts, or
 *  - falls back to photographing the nutrition label (read by the vision model) when OFF has no match.
 * Manual barcode entry is offered for cameras that can't be used (permissions, desktop).
 */
export function ScanBarcodePage() {
  const navigate = useNavigate()
  const videoRef = useRef<HTMLVideoElement>(null)
  const controlsRef = useRef<IScannerControls | null>(null)
  const labelInputRef = useRef<HTMLInputElement>(null)

  const [phase, setPhase] = useState<Phase>('scanning')
  const [error, setError] = useState<string | null>(null)
  const [cameraOk, setCameraOk] = useState(true)
  const [manualCode, setManualCode] = useState('')
  // The barcode that failed OFF lookup — carried onto the label-OCR path so the new product is still
  // contributed back to OFF under its real code.
  const [pendingBarcode, setPendingBarcode] = useState<string | null>(null)

  // Start the camera scanner while in the scanning phase; stop it whenever we leave that phase or unmount.
  useEffect(() => {
    if (phase !== 'scanning') return
    if (!navigator.mediaDevices?.getUserMedia) { setCameraOk(false); return }

    let cancelled = false
    const reader = new BrowserMultiFormatReader()
    reader
      .decodeFromVideoDevice(undefined, videoRef.current ?? undefined, (result, _err, controls) => {
        controlsRef.current = controls
        if (result && !cancelled) {
          controls.stop()
          void handleBarcode(result.getText())
        }
      })
      .then((controls) => { controlsRef.current = controls })
      .catch(() => { if (!cancelled) setCameraOk(false) })

    return () => {
      cancelled = true
      controlsRef.current?.stop()
      controlsRef.current = null
    }
    // handleBarcode is stable enough for this effect's purpose; re-running only on phase change is intended.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase])

  // Resolve a scanned/typed barcode.
  async function handleBarcode(code: string) {
    setError(null)
    setPhase('looking-up')
    try {
      const result = await ingredientApi.byBarcode(code)

      if (result.existingIngredientId) {
        // Already in the library — open it instead of creating a duplicate.
        navigate(`/ingredients/${result.existingIngredientId}`, { replace: true })
        return
      }

      if (result.found) {
        // The backend rejects OFF records with physically impossible values (bad community data), so a
        // found product can arrive with no nutrition — prefill the identity fields and say so honestly.
        const hasNutrition = result.nutritionPer100g.length > 0
        const prefill: ScanPrefill = {
          name: result.productName,
          barcode: result.barcode,
          imageUrl: result.imageUrl,
          // OFF data is per 100 g.
          unit: 'Gram',
          servingSize: 100,
          nutritionPerServing: result.nutritionPer100g,
          note: hasNutrition
            ? `Filled from ${result.source ?? 'Open Food Facts'}` +
              (result.productName ? ` ("${result.productName}"` : '') +
              (result.productName ? ', per 100 g). Review and edit before saving.' : ' (per 100 g). Review and edit before saving.')
            : `Found "${result.productName ?? result.barcode}" on ${result.source ?? 'Open Food Facts'}, but its ` +
              'nutrition data looked unreliable and was not imported. Enter the values from the package ' +
              '(or use "Look up nutrition").',
        }
        navigate('/ingredients/new', { replace: true, state: { scanPrefill: prefill } })
        return
      }

      // Not found on OFF — offer the label-photo path.
      setPendingBarcode(result.barcode)
      setPhase('not-found')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Lookup failed.')
      setPhase('not-found')
    }
  }

  function submitManual() {
    const code = manualCode.trim()
    if (code) void handleBarcode(code)
  }

  // Read a photographed nutrition label and pre-fill the form from it.
  async function handleLabel(file: File) {
    setError(null)
    setPhase('reading-label')
    try {
      const result = await ocrApi.readLabel(file)
      const prefill: ScanPrefill = {
        name: result.productName,
        barcode: pendingBarcode ?? undefined,
        unit: result.unit,
        servingSize: result.servingSize,
        nutritionPerServing: result.nutritionPerServing,
        note: `Read ${result.nutritionPerServing.length} nutrient` +
          `${result.nutritionPerServing.length === 1 ? '' : 's'} from the label photo. ` +
          'Review and edit before saving.',
      }
      navigate('/ingredients/new', { replace: true, state: { scanPrefill: prefill } })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not read the label. Enter the values manually.')
      setPhase('not-found')
    } finally {
      if (labelInputRef.current) labelInputRef.current.value = ''
    }
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Scan a product</h1>
        <button className="btn btn-sm" onClick={() => navigate('/ingredients')}>Cancel</button>
      </div>

      {error && <div className="notice error">{error}</div>}

      {phase === 'scanning' && (
        <div className="card stack">
          {cameraOk ? (
            <>
              <p className="muted small" style={{ margin: 0 }}>
                Point your camera at the barcode on the package.
              </p>
              <video ref={videoRef} playsInline muted
                style={{ width: '100%', borderRadius: 'var(--radius, 8px)', background: '#000' }} />
            </>
          ) : (
            <div className="notice info">
              Camera isn't available. Enter the barcode number printed under the bars instead.
            </div>
          )}

          <div className="term-add">
            <input className="input" inputMode="numeric" value={manualCode} placeholder="Barcode number"
              onChange={(e) => setManualCode(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); submitManual() } }} />
            <button type="button" className="btn btn-sm" onClick={submitManual} disabled={!manualCode.trim()}>
              Look up
            </button>
          </div>
        </div>
      )}

      {phase === 'looking-up' && <p className="muted">Looking up the product…</p>}
      {phase === 'reading-label' && <p className="muted">Reading the nutrition label…</p>}

      {phase === 'not-found' && (
        <div className="card stack">
          <div className="notice info">
            {pendingBarcode
              ? `We couldn't find that product (barcode ${pendingBarcode}).`
              : 'No product data found.'}
            {' '}Take a photo of the Nutrition Facts label and we'll read it. Once your product is approved
            for the public library, it's also shared back to Open Food Facts so the next person's scan finds it.
          </div>
          <input
            ref={labelInputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp,image/gif"
            capture="environment"
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void handleLabel(f) }}
          />
          <div className="row" style={{ gap: '0.5rem' }}>
            <button className="btn btn-sm" onClick={() => { setPendingBarcode(null); setError(null); setPhase('scanning') }}>
              Scan again
            </button>
            <button className="btn btn-sm"
              onClick={() => navigate('/ingredients/new', {
                replace: true,
                state: pendingBarcode
                  ? { scanPrefill: { barcode: pendingBarcode, unit: 'Gram', servingSize: 100, nutritionPerServing: [], note: 'Enter this product’s details manually.' } satisfies ScanPrefill }
                  : undefined,
              })}>
              Enter manually
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
