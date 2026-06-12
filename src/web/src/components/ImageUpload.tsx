import { useRef, useState } from 'react'
import { imageApi } from '../api/services'
import { IconMeal } from './icons'

// Mirror of the server-side validation so we fail fast before uploading.
const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp', 'image/gif']
const MAX_SIZE_BYTES = 5 * 1024 * 1024

interface ImageUploadProps {
  /** Current image URL (stored in the entity's imageUrl field), or empty when none. */
  value: string
  /** Called with the new URL after a successful upload, or '' when cleared. */
  onChange: (url: string) => void
  disabled?: boolean
}

/**
 * Reusable image picker: shows a preview, validates type/size on the client, uploads to the
 * `/images` endpoint, and reports the resulting blob URL via onChange. Used by the Ingredient
 * and Recipe forms in place of the old plain image-URL text field.
 */
export function ImageUpload({ value, onChange, disabled }: ImageUploadProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleFile(file: File) {
    setError(null)
    if (!ACCEPTED_TYPES.includes(file.type)) {
      setError('Unsupported image type. Use JPEG, PNG, WebP, or GIF.')
      return
    }
    if (file.size > MAX_SIZE_BYTES) {
      setError('Image exceeds the 5 MB size limit.')
      return
    }

    setUploading(true)
    try {
      const { url } = await imageApi.upload(file)
      onChange(url)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Upload failed.')
    } finally {
      setUploading(false)
      // Reset so the same file can be re-selected after an error.
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  function clear() {
    onChange('')
    setError(null)
  }

  return (
    <div className="field">
      <span>Image</span>
      <div className="image-upload">
        <div className="ingredient-thumb image-upload-preview">
          {value ? <img src={value} alt="" /> : <IconMeal size={26} />}
        </div>

        <div className="stack" style={{ gap: '0.4rem' }}>
          <input
            ref={inputRef}
            type="file"
            accept={ACCEPTED_TYPES.join(',')}
            disabled={disabled || uploading}
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void handleFile(f) }}
          />
          <div className="row" style={{ gap: '0.4rem' }}>
            {uploading && <span className="muted small">Uploading…</span>}
            {value && !uploading && !disabled && (
              <button type="button" className="btn btn-sm" onClick={clear}>Remove</button>
            )}
          </div>
        </div>
      </div>
      {error && <span className="notice error small">{error}</span>}
    </div>
  )
}
