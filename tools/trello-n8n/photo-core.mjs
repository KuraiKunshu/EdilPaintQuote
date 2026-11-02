// Functions shared by the worker and offline tests. Credentials never enter item data.
export const MAX_PHOTO_BYTES = 25 * 1024 * 1024;
export const MAX_PHOTOS_PER_REQUEST = 100;
export const MAX_REQUEST_BYTES = 200 * 1024 * 1024;

export function photoError(request, message) {
  return { CardId: request.CardId, RequestedAtUtc: request.RequestedAtUtc, Mode: 'error', Error: message };
}

export function preparePhotoRequest(request) {
  try {
    if (!/^[a-f0-9]{24}$/.test(request.CardId) || typeof request.RequestedAtUtc !== 'string' || Number.isNaN(Date.parse(request.RequestedAtUtc)))
      throw new Error('Richiesta fotografie non valida.');
    const expected = JSON.parse(request.ExpectedAttachmentIdsJson);
    if (!Array.isArray(expected) || expected.some(id => !/^[a-f0-9]{24}$/.test(id)) || new Set(expected).size !== expected.length)
      throw new Error('Elenco delle fotografie richieste non valido.');
    if (expected.length > MAX_PHOTOS_PER_REQUEST) throw new Error('La scheda supera il limite di 100 fotografie per richiesta.');
    if (!expected.length) return [{ CardId: request.CardId, RequestedAtUtc: request.RequestedAtUtc, Mode: 'empty', Error: '' }];
    const payload = JSON.parse(request.PayloadJson);
    if (!Array.isArray(payload.attachments)) throw new Error('Metadati degli allegati non disponibili. Sincronizzare la scheda Trello.');
    const attachments = new Map(payload.attachments.map(attachment => [attachment.id, attachment]));
    const photos = expected.map(id => {
      const attachment = attachments.get(id);
      if (!attachment || attachment.isUpload !== true) throw new Error('Una fotografia richiesta non è più disponibile negli allegati Trello.');
      let fileName = attachment.fileName;
      if (!fileName && typeof attachment.url === 'string') {
        try {
          const source = new URL(attachment.url);
          fileName = decodeURIComponent(source.pathname.split('/').at(-1));
        } catch { /* Legacy metadata can still contain the original name. */ }
      }
      if (!fileName) fileName = attachment.name;
      if (typeof fileName !== 'string' || !fileName || fileName.includes('/') || fileName.includes('\\') || /[\u0000-\u001f]/.test(fileName))
        throw new Error('Nome del file fotografico non valido.');
      let mime = typeof attachment.mimeType === 'string' ? attachment.mimeType.toLowerCase().split(';')[0].trim() : '';
      if (mime === 'image/jpg') mime = 'image/jpeg';
      const inferred = ({ jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp', gif: 'image/gif', bmp: 'image/bmp' })[fileName.split('.').at(-1).toLowerCase()];
      const contentType = mime || inferred;
      if (!['image/jpeg', 'image/png', 'image/webp', 'image/gif', 'image/bmp'].includes(contentType))
        throw new Error('Una fotografia usa un formato non supportato. Usare JPEG, PNG, WebP, GIF o BMP.');
      if (!Number.isSafeInteger(attachment.bytes) || attachment.bytes <= 0 || attachment.bytes > MAX_PHOTO_BYTES)
        throw new Error('Dimensione della fotografia non valida o superiore a 25 MB.');
      return {
        CardId: request.CardId, RequestedAtUtc: request.RequestedAtUtc, Mode: 'photo', Error: '', AttachmentId: id,
        FileName: fileName, ContentType: contentType, ExpectedBytes: attachment.bytes,
        SourceUrl: 'https://api.trello.com/1/cards/' + request.CardId + '/attachments/' + id + '/download/' + encodeURIComponent(fileName),
      };
    });
    if (photos.reduce((bytes, photo) => bytes + photo.ExpectedBytes, 0) > MAX_REQUEST_BYTES)
      throw new Error('Le fotografie della scheda superano il limite di 200 MB per richiesta.');
    return photos;
  } catch (error) {
    return [photoError(request, error instanceof SyntaxError ? 'Metadati delle fotografie non leggibili. Sincronizzare la scheda Trello.' : error.message)];
  }
}

export function classifyPhotoResponse(photo, response, hasBinary) {
  const status = Number(response?.statusCode);
  if (status === 200 && hasBinary) return { ...photo, statusCode: status, SignedUrl: '', Error: '' };
  if (status !== 302 && status !== 303 && status !== 307)
    return photoError(photo, 'Download Trello non riuscito. Verificare la credenziale delle fotografie.');
  const headers = response.headers || {};
  const key = Object.keys(headers).find(name => name.toLowerCase() === 'location');
  try {
    const signed = new URL(headers[key]);
    // Only the signed storage URL returned by authenticated Trello is downloaded without credentials.
    if (signed.protocol !== 'https:' || signed.username || signed.password || (signed.port && signed.port !== '443') ||
        !signed.hostname.toLowerCase().endsWith('.amazonaws.com')) throw new Error('Unexpected storage host');
    return { ...photo, SignedUrl: signed.href, Error: '' };
  } catch {
    return photoError(photo, 'Trello ha restituito un collegamento di download non valido.');
  }
}

export function imageMime(bytes) {
  if (bytes.length >= 8 && bytes[0] === 0x89 && Buffer.from(bytes.subarray(1, 4)).toString('ascii') === 'PNG' &&
      bytes[4] === 13 && bytes[5] === 10 && bytes[6] === 26 && bytes[7] === 10) return 'image/png';
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg';
  if (bytes.length >= 6 && ['GIF87a', 'GIF89a'].includes(Buffer.from(bytes.subarray(0, 6)).toString('ascii'))) return 'image/gif';
  if (bytes.length >= 12 && Buffer.from(bytes.subarray(0, 4)).toString('ascii') === 'RIFF' && Buffer.from(bytes.subarray(8, 12)).toString('ascii') === 'WEBP') return 'image/webp';
  if (bytes.length >= 2 && bytes[0] === 0x42 && bytes[1] === 0x4d) return 'image/bmp';
  return '';
}

export function encodePhoto(photo, response, bytes, downloadedAtUtc) {
  if (photo.Error) return photoError(photo, photo.Error);
  if (Number(response?.statusCode) !== 200 || !bytes || !bytes.length || bytes.length > MAX_PHOTO_BYTES || bytes.length !== photo.ExpectedBytes)
    return photoError(photo, 'Fotografia incompleta o non scaricabile. La scheda lavoro non può essere stampata con fotografie mancanti.');
  const mime = imageMime(bytes);
  if (!mime || mime !== photo.ContentType)
    return photoError(photo, 'Il contenuto scaricato non è una fotografia valida nel formato previsto.');
  return {
    CardId: photo.CardId, RequestedAtUtc: photo.RequestedAtUtc, Mode: 'photo', Error: '', AttachmentId: photo.AttachmentId,
    FileName: photo.FileName, ContentType: mime, ContentBase64: Buffer.from(bytes).toString('base64'),
    SourceUrl: photo.SourceUrl, SyncedAtUtc: new Date(downloadedAtUtc).toISOString(),
  };
}
