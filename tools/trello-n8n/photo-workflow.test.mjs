import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { preparePhotoRequest, classifyPhotoResponse, encodePhoto, imageMime, MAX_PHOTO_BYTES, MAX_REQUEST_BYTES } from './photo-core.mjs';

const cardId = '64a111111111111111111111';
const attachmentId = '64b222222222222222222222';
const epoch = '2026-11-02 10:44:31.240123+00';
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4]);
const sourceAttachment = { id: attachmentId, isUpload: true, fileName: 'foto ingresso.jpg', name: 'Nome rinominabile',
  mimeType: 'image/jpeg', bytes: jpeg.length, url: 'https://trello.com/1/cards/' + cardId + '/attachments/' + attachmentId + '/download/original.jpg' };
const request = (attachments = [sourceAttachment], ids = [attachmentId]) => ({ CardId: cardId, RequestedAtUtc: epoch,
  ExpectedAttachmentIdsJson: JSON.stringify(ids), PayloadJson: JSON.stringify({ attachments }) });
const workflow = JSON.parse(fs.readFileSync(new URL('./photo-workflow.json', import.meta.url)));
const byName = name => workflow.nodes.find(node => node.name === name);

test('worker downloads only explicitly requested uploaded raster photos and preserves request microseconds', () => {
  const unrelated = { ...sourceAttachment, id: '64c333333333333333333333', fileName: 'altra.jpg' };
  const [photo] = preparePhotoRequest(request([sourceAttachment, unrelated]));
  assert.equal(photo.AttachmentId, attachmentId);
  assert.equal(photo.RequestedAtUtc, epoch);
  assert.equal(photo.SourceUrl, 'https://api.trello.com/1/cards/' + cardId + '/attachments/' + attachmentId + '/download/foto%20ingresso.jpg');
  assert.ok(!JSON.stringify(photo).includes('Nome rinominabile'));
  assert.equal(photo.Mode, 'photo');
});

test('no photos finishes via an explicit empty control item, never a missing loop branch', () => {
  assert.equal(preparePhotoRequest(request([], []))[0].Mode, 'empty');
  assert.deepEqual(preparePhotoRequest(request([], []))[0], { CardId: cardId, RequestedAtUtc: epoch, Mode: 'empty', Error: '' });
});

test('JPEG MIME aliases and parameters normalize before downloading and validating bytes', () => {
  for (const mimeType of ['image/jpg', 'IMAGE/JPG; charset=binary', ' IMAGE/JPEG ; charset=binary']) {
    const photo = preparePhotoRequest(request([{ ...sourceAttachment, mimeType }]))[0];
    assert.equal(photo.Mode, 'photo');
    assert.equal(photo.ContentType, 'image/jpeg');
    assert.equal(encodePhoto(photo, { statusCode: 200 }, jpeg, '2026-11-02T10:45:00Z').Mode, 'photo');
  }
});

test('canonical file name wins over URL, which wins over the legacy attachment name', () => {
  const canonical = preparePhotoRequest(request([{ ...sourceAttachment, fileName: 'original.jpg' }]))[0];
  assert.ok(canonical.SourceUrl.endsWith('/download/original.jpg'));
  const fromUrl = preparePhotoRequest(request([{ ...sourceAttachment, fileName: '', mimeType: '', name: 'Rinominata senza estensione',
    url: 'https://trello.com/1/cards/card/attachments/attachment/download/foto%20originale.jpg' }]))[0];
  assert.equal(fromUrl.Mode, 'photo');
  assert.equal(fromUrl.FileName, 'foto originale.jpg');
  assert.equal(fromUrl.ContentType, 'image/jpeg');
  const legacy = preparePhotoRequest(request([{ ...sourceAttachment, fileName: '', url: '', mimeType: '', name: 'legacy.JPG' }]))[0];
  assert.equal(legacy.Mode, 'photo');
  assert.equal(legacy.FileName, 'legacy.JPG');
});

test('missing, external, unsupported, oversized and duplicate photos produce a failed request without partial photos', () => {
  for (const input of [request([]), request([{ ...sourceAttachment, isUpload: false }]),
    request([{ ...sourceAttachment, mimeType: 'image/svg+xml' }]), request([{ ...sourceAttachment, mimeType: 'image/heic' }]),
    request([{ ...sourceAttachment, bytes: MAX_PHOTO_BYTES + 1 }]), request([sourceAttachment], [attachmentId, attachmentId]),
    { ...request(), PayloadJson: 'invalid' }]) {
    const output = preparePhotoRequest(input);
    assert.equal(output.length, 1); assert.equal(output[0].Mode, 'error'); assert.ok(output[0].Error);
  }
  const many = Array.from({ length: 9 }, (_, i) => ({ ...sourceAttachment, id: i.toString(16).padStart(24, '0'), bytes: MAX_PHOTO_BYTES }));
  assert.ok(many.reduce((n, p) => n + p.bytes, 0) > MAX_REQUEST_BYTES);
  assert.equal(preparePhotoRequest(request(many, many.map(p => p.id)))[0].Mode, 'error');
});

test('a direct 200 binary response is accepted, redirects accept only HTTPS storage returned by Trello', () => {
  const photo = preparePhotoRequest(request())[0];
  assert.equal(classifyPhotoResponse(photo, { statusCode: 200 }, true).SignedUrl, '');
  assert.equal(classifyPhotoResponse(photo, { statusCode: 302, headers: { Location: 'https://trello-attachments.s3.amazonaws.com/file.jpg?signature=test' } }, true).SignedUrl,
    'https://trello-attachments.s3.amazonaws.com/file.jpg?signature=test');
  assert.ok(classifyPhotoResponse(photo, { statusCode: 302, headers: { location: 'https://trello-attachments.s3.amazonaws.com/file.jpg' } }, true).SignedUrl);
  for (const location of ['http://trello-attachments.s3.amazonaws.com/file.jpg', 'https://api.trello.com/file', 'https://amazonaws.com.attacker.test/file', 'http://127.0.0.1/file', 'https://user:password@bucket.s3.amazonaws.com/file'])
    assert.equal(classifyPhotoResponse(photo, { statusCode: 302, headers: { location } }, true).Mode, 'error');
  assert.equal(classifyPhotoResponse(photo, { statusCode: 401 }, false).Mode, 'error');
  assert.equal(classifyPhotoResponse(photo, { statusCode: 200 }, false).Mode, 'error');
});

test('base64 photo payload validates status, declared size and raster magic and does not preserve signed URLs', () => {
  const photo = preparePhotoRequest(request())[0];
  const result = encodePhoto({ ...photo, SignedUrl: 'https://bucket.s3.amazonaws.com/file?private=signature' }, { statusCode: 200 }, jpeg, '2026-11-02T10:45:00Z');
  assert.equal(result.ContentBase64, jpeg.toString('base64')); assert.equal(result.ContentType, 'image/jpeg');
  assert.equal(result.RequestedAtUtc, epoch); assert.ok(!JSON.stringify(result).includes('private=signature'));
  for (const [response, bytes] of [[{ statusCode: 404 }, jpeg], [{ statusCode: 200 }, jpeg.subarray(0, 3)], [{ statusCode: 200 }, Buffer.from('<html>ab')]])
    assert.equal(encodePhoto(photo, response, bytes, '2026-11-02T10:45:00Z').Mode, 'error');
});

test('embedded direct-download path retains HTTP status and binary data through classification and verification', async () => {
  const second = { ...sourceAttachment, id: '64c333333333333333333333', fileName: 'seconda.jpg' };
  const photos = preparePhotoRequest(request([sourceAttachment, second], [attachmentId, second.id]));
  const responses = photos.map(() => ({ json: { statusCode: 200, headers: {} }, binary: { data: { id: 'binary-store-reference' } } }));
  const classified = vm.runInNewContext('(function(){' + byName('Controlla risposta Trello').parameters.jsCode + '})()', {
    $input: { all: () => responses }, $: () => ({ itemMatching: index => ({ json: photos[index] }) }), URL, Buffer,
  });
  for (const item of classified) {
    assert.equal(item.json.statusCode, 200);
    assert.ok(item.binary.data);
  }
  const result = await vm.runInNewContext('(async function(){' + byName('Verifica fotografia diretta').parameters.jsCode + '}).call(context)', {
    $input: { all: () => classified }, $: () => ({ itemMatching: index => classified[index] }), URL, Buffer,
    context: { helpers: { getBinaryDataBuffer: async (index, property) => {
      assert.equal(property, 'data');
      assert.ok(classified[index].binary.data);
      return jpeg;
    } } },
  });
  assert.equal(result.length, photos.length);
  result.forEach((item, index) => {
    assert.equal(item.json.Mode, 'photo');
    assert.equal(item.json.Error, '');
    assert.equal(item.json.AttachmentId, photos[index].AttachmentId);
    assert.equal(item.json.ContentBase64, jpeg.toString('base64'));
    assert.equal(item.pairedItem.item, index);
  });
});

test('raster recognizer covers printable formats and rejects SVG HTML PDF HEIC and arbitrary binary', () => {
  assert.equal(imageMime(Buffer.from([0x89, 80, 78, 71, 13, 10, 26, 10])), 'image/png');
  assert.equal(imageMime(Buffer.from('GIF89a')), 'image/gif');
  assert.equal(imageMime(Buffer.from('RIFF0000WEBP')), 'image/webp');
  assert.equal(imageMime(Buffer.from('BM')), 'image/bmp');
  for (const bytes of [Buffer.from('<svg />'), Buffer.from('<html>'), Buffer.from('%PDF'), Buffer.from('0000ftypheic'), Buffer.alloc(20)])
    assert.equal(imageMime(bytes), '');
});

test('workflow binds credentials only to Trello GET, never follows redirects or authenticates storage', () => {
  const auth = byName('Scarica da Trello con autorizzazione');
  const plain = byName('Scarica fotografia senza credenziali');
  assert.equal(auth.parameters.genericAuthType, 'httpHeaderAuth');
  assert.equal(auth.parameters.options.redirect.redirect.followRedirects, false);
  assert.equal(auth.parameters.options.sendCredentialsOnCrossOriginRedirect, false);
  assert.equal(auth.parameters.options.response.response.responseFormat, 'file');
  assert.equal(auth.parameters.options.response.response.fullResponse, true);
  assert.ok(!auth.continueOnFail); // n8n's continueOnFail can emit a raw HTTP error object into item data.
  assert.equal(plain.parameters.authentication, 'none');
  for (const node of workflow.nodes) {
    assert.ok(!node.credentials);
    if (node.type === 'n8n-nodes-base.httpRequest') assert.equal(node.parameters.method, 'GET');
    if (node.type === 'n8n-nodes-base.postgres') {
      assert.ok(!/(?:UPDATE|INSERT\s+INTO|DELETE\s+FROM)\s+(?:public\.)?"?(?:Quotes|Customers)"?/i.test(node.parameters.query));
    }
  }
  assert.equal(workflow.active, false);
  assert.equal(workflow.settings.saveDataSuccessExecution, 'none');
  assert.equal(workflow.settings.saveDataErrorExecution, 'none');
});

test('SQL claims atomically and cannot close or overwrite a replacement request', () => {
  const claim = byName('Prendi una richiesta').parameters.query;
  const save = byName('Salva foto e stato richiesta').parameters.query;
  assert.match(claim, /FOR UPDATE SKIP LOCKED/); assert.match(claim, /LIMIT 1/);
  assert.match(claim, /"StartedAtUtc" < now\(\) - interval '15 minutes'/);
  assert.match(claim, /"RequestedAtUtc"::text AS "RequestedAtUtc"/);
  assert.match(save, /\$1::jsonb/); assert.match(save, /FOR UPDATE OF request/);
  assert.match(save, /"RequestedAtUtc" = \(data->>'RequestedAtUtc'\)::timestamptz/);
  assert.match(save, /"ExpectedAttachmentIdsJson"::jsonb \? \(data->>'AttachmentId'\)/);
  assert.match(save, /FROM stored WHERE/);
  assert.match(save, /"SyncedAtUtc" >= request\."RequestedAtUtc"/);
  assert.equal(byName('Salva foto e stato richiesta').parameters.options.queryReplacement, '={{ [JSON.stringify($json)] }}');
});

test('embedded preparation code produces paired outputs without using network or credentials', () => {
  const node = byName('Prepara fotografie richieste');
  const output = vm.runInNewContext('(function(){' + node.parameters.jsCode + '})()', {
    $input: { all: () => [{ json: request() }] }, URL, Buffer,
  });
  assert.equal(output[0].json.AttachmentId, attachmentId);
  assert.equal(output[0].pairedItem.item, 0);
  for (const codeNode of workflow.nodes.filter(n => n.type === 'n8n-nodes-base.code'))
    assert.ok(!/\bfetch\s*\(|httpRequest\s*\(|getCredentials\s*\(|\$credentials/.test(codeNode.parameters.jsCode));
});
