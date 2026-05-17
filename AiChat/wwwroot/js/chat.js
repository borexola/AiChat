window.scrollToBottom = (element) => {
    if (element && element.scrollHeight) {
        element.scrollTop = element.scrollHeight;
    }
};

window.autoResizeTextarea = (element, maxHeight) => {
    if (!element) return;
    element.style.height = 'auto';
    const newHeight = Math.min(element.scrollHeight, maxHeight);
    element.style.height = newHeight + 'px';
};

window.getFileList = (inputElement) => {
    const files = [];
    for (let i = 0; i < inputElement.files.length; i++) {
        const f = inputElement.files[i];
        files.push({ name: f.name, size: f.size, type: f.type });
    }
    return files;
};

window.triggerFileInputClickById = (elementId) => {
    document.getElementById(elementId)?.click();
};

window.copyTextToClipboard = async (text) => {
    if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(text);
        return;
    }
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    document.execCommand('copy');
    document.body.removeChild(textarea);
};

window.focusElement = (element) => {
    if (element) {
        element.focus();
        const len = element.value?.length ?? 0;
        element.setSelectionRange(len, len);
    }
};

// Wire up a drop zone so dropped files are forwarded to the hidden InputFile element.
// Blazor's @ondrop can't call preventDefault fast enough (server round-trip), so we
// handle the entire drag/drop lifecycle here in JS.
window.initDropZone = (dropZoneId, fileInputId) => {
    const zone = document.getElementById(dropZoneId);
    const input = document.getElementById(fileInputId);
    if (!zone || !input) return;

    ['dragenter', 'dragover'].forEach(evt => {
        zone.addEventListener(evt, (e) => {
            e.preventDefault();
            e.stopPropagation();
            zone.classList.add('dragging');
        });
    });

    ['dragleave', 'dragend'].forEach(evt => {
        zone.addEventListener(evt, (e) => {
            e.preventDefault();
            zone.classList.remove('dragging');
        });
    });

    zone.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        zone.classList.remove('dragging');
        const files = e.dataTransfer?.files;
        if (!files || files.length === 0) return;
        const dt = new DataTransfer();
        for (const file of files) dt.items.add(file);
        input.files = dt.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
};

// Intercept paste events on the given textarea; when an image is in the clipboard
// it is forwarded to a hidden InputFile element so Blazor receives a proper IBrowserFile.
window.initPasteImageHandler = (textareaElement, dotNetRef) => {
    if (!textareaElement) return;

    textareaElement._pasteImageDotNetRef = dotNetRef;

    if (textareaElement.dataset.pasteImageHandlerAttached === 'true') {
        return;
    }

    textareaElement.dataset.pasteImageHandlerAttached = 'true';
    textareaElement.addEventListener('paste', async (e) => {
        const items = e.clipboardData?.items;
        if (!items) return;
        let handled = false;
        let imageIndex = 0;

        for (const item of items) {
            if (!item.type.startsWith('image/')) continue;
            const blob = item.getAsFile();
            if (!blob) continue;
            const ext = (item.type.split('/')[1]?.split('+')[0]) || 'png';
            const fileName = `pasted-${Date.now()}-${imageIndex++}.${ext}`;
            const reader = new FileReader();

            await new Promise((resolve) => {
                reader.onload = async (event) => {
                    const dataUrl = event.target?.result;
                    const currentDotNetRef = textareaElement._pasteImageDotNetRef;
                    if (typeof dataUrl === 'string' && currentDotNetRef) {
                        await currentDotNetRef.invokeMethodAsync('HandlePastedImage', fileName, item.type, dataUrl);
                    }
                    resolve();
                };

                reader.onerror = () => resolve();
                reader.readAsDataURL(blob);
            });

            handled = true;
        }

        if (handled) {
            e.preventDefault();
        }
    });
};

// Returns a base64 data-URL for the first file in an <input type=file> element, or null.
window.getFilePreviewUrl = (inputId) => {
    return new Promise((resolve) => {
        const input = document.getElementById(inputId);
        const file = input?.files?.[0];
        if (!file || !file.type.startsWith('image/')) { resolve(null); return; }
        const reader = new FileReader();
        reader.onload = (e) => resolve(e.target.result);
        reader.onerror = () => resolve(null);
        reader.readAsDataURL(file);
    });
};

// ── Image modal ──────────────────────────────────────────────────────────────
// Opens a full-screen modal showing the image at the given src.
window.openImageModal = (src, alt) => {
    if (document.getElementById('_imgModal')) return;

    const backdrop = document.createElement('div');
    backdrop.id = '_imgModal';
    backdrop.className = 'img-modal-backdrop';
    backdrop.setAttribute('role', 'dialog');
    backdrop.setAttribute('aria-modal', 'true');

    const inner = document.createElement('div');
    inner.className = 'img-modal-inner';

    const img = document.createElement('img');
    img.src = src;
    img.alt = alt || '';

    const closeBtn = document.createElement('button');
    closeBtn.className = 'img-modal-close';
    closeBtn.innerHTML = '&times;';
    closeBtn.title = 'Close';
    closeBtn.addEventListener('click', (e) => { e.stopPropagation(); window.closeImageModal(); });

    inner.appendChild(closeBtn);
    inner.appendChild(img);
    backdrop.appendChild(inner);

    backdrop.addEventListener('click', (e) => {
        if (e.target === backdrop) window.closeImageModal();
    });

    document.addEventListener('keydown', _imgModalKeyHandler);
    document.body.appendChild(backdrop);
};

window.closeImageModal = () => {
    document.getElementById('_imgModal')?.remove();
    document.removeEventListener('keydown', _imgModalKeyHandler);
};

function _imgModalKeyHandler(e) {
    if (e.key === 'Escape') window.closeImageModal();
}

// Wire up all img elements inside .message-content to open the modal on click.
// Called once after the Blazor component renders so new messages are covered.
window.wireMessageImages = (container) => {
    if (!container) return;
    container.querySelectorAll('.message-content img').forEach(img => {
        if (img.dataset.modalWired) return;
        img.dataset.modalWired = 'true';
        img.addEventListener('click', () => window.openImageModal(img.src, img.alt));
    });
};

// Download a file from a data-URL with the given filename.
window.downloadDataUrl = (dataUrl, filename) => {
    const a = document.createElement('a');
    a.href = dataUrl;
    a.download = filename || 'download';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};
