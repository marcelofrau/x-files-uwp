var editor = (function () {
    'use strict';

    var codeEl = null;
    var editorEl = null;
    var lineNumbersEl = null;
    var blockCursorEl = null;
    var mouseCursorEl = null;
    var _mouseX = 100;
    var _mouseY = 100;
    var _dirty = false;
    var _wordWrap = false;
    var _highlightEnabled = true;
    var _language = '';
    var _highlightTimeout = null;
    var _undoStack = [];
    var _redoStack = [];
    var _maxUndo = 0; // 0 = unlimited
    var _lastContent = '';
    var _cursorBlinkInterval = null;
    var _cursorVisible = true;
    var _charWidth = 0;

    // ── Debug logging ─────────────────────────────────────────
    var _logBuffer = [];
    var _logMax = 200;

    function _dbg(op, extra) {
        var pos = getCursorPosition();
        var sel = window.getSelection();
        var selLen = 0;
        if (sel && sel.rangeCount > 0) {
            selLen = sel.getRangeAt(0).toString().length;
        }
        var text = getText();
        var lines = text.split('\n');
        var line = 0, col = 0, charsBefore = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBefore + lines[i].length >= pos) { line = i; col = pos - charsBefore; break; }
            charsBefore += lines[i].length + 1;
        }
        var msg = '[' + op + '] pos=' + pos + ' line=' + line + ' col=' + col + ' sel=' + selLen + ' lines=' + lines.length + ' chars=' + text.length;
        if (extra) msg += ' ' + extra;
        _logBuffer.push(msg);
        if (_logBuffer.length > _logMax) _logBuffer.shift();
        console.log('ED ' + msg);
    }

    function _getLogs() {
        var result = _logBuffer.join('\n');
        _logBuffer = [];
        return result;
    }

    function init() {
        codeEl = document.getElementById('code');
        editorEl = document.getElementById('editor');
        lineNumbersEl = document.getElementById('line-numbers');

        if (!codeEl || !editorEl) return;

        editorEl.setAttribute('spellcheck', 'false');

        // Create block cursor element
        blockCursorEl = document.createElement('div');
        blockCursorEl.id = 'block-cursor';
        blockCursorEl.style.cssText = 'position:absolute;width:8.5px;background:#93C43C;opacity:1;pointer-events:none;z-index:10;transition:none;display:none;';
        editorEl.style.position = 'relative';
        editorEl.appendChild(blockCursorEl);

        // Measure actual char width for block cursor
        var measure = document.createElement('span');
        measure.style.cssText = 'font:13px Inconsolata,Consolas,Courier New,monospace;position:absolute;visibility:hidden;white-space:pre;';
        measure.textContent = 'M';
        document.body.appendChild(measure);
        _charWidth = measure.getBoundingClientRect().width || 8.5;
        document.body.removeChild(measure);
        blockCursorEl.style.width = _charWidth + 'px';

        // Create mouse cursor element (LStick pointer on Xbox)
        mouseCursorEl = document.createElement('div');
        mouseCursorEl.id = 'mouse-cursor';
        // I-beam cursor: thin vertical line with top/bottom caps
        mouseCursorEl.innerHTML = '<svg width="12" height="20" viewBox="0 0 12 20" style="margin-left:-6px;margin-top:-10px;filter:drop-shadow(0 0 1px rgba(0,0,0,0.9))">' +
            '<line x1="6" y1="0" x2="6" y2="20" stroke="#fff" stroke-width="1.5"/>' +
            '<line x1="2" y1="0" x2="10" y2="0" stroke="#fff" stroke-width="1.5"/>' +
            '<line x1="2" y1="19" x2="10" y2="19" stroke="#fff" stroke-width="1.5"/>' +
            '</svg>';
        mouseCursorEl.style.cssText = 'position:fixed;pointer-events:none;z-index:999;display:none;';
        document.body.appendChild(mouseCursorEl);

        // Start cursor blink
        startCursorBlink();

        // Normalize EdgeHTML <div> → \n on every DOM mutation
        var observer = new MutationObserver(function () {
            normalizeContent();
            updateLineNumbers();
        });
        observer.observe(codeEl, { childList: true, characterData: true, subtree: true });

        // Intercept paste: plain text only
        editorEl.addEventListener('paste', function (e) {
            e.preventDefault();
            var text = (e.clipboardData || window.clipboardData).getData('text/plain');
            document.execCommand('insertText', false, text);
        });

        // Track dirty state on input (listen on contentEditable div, not <code>)
        editorEl.addEventListener('input', function () {
            _dirty = true;
            pushUndoState();
            scheduleHighlight();
            updateLineNumbers();
        });

        // Keyboard shortcuts
        codeEl.addEventListener('keydown', function (e) {
            // Ctrl+Z = undo
            if (e.ctrlKey && e.key === 'z') { e.preventDefault(); undo(); }
            // Ctrl+Y = redo
            if (e.ctrlKey && e.key === 'y') { e.preventDefault(); redo(); }
        });

        // Update block cursor on selection changes
        document.addEventListener('selectionchange', function () {
            updateBlockCursor();
        });

        // Update on click
        codeEl.addEventListener('click', function () {
            updateBlockCursor();
        });

        updateLineNumbers();

        // Focus codeEl so cursor is visible immediately
        codeEl.focus();
        // Place cursor at start if no selection
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) {
            var range = document.createRange();
            range.selectNodeContents(codeEl);
            range.collapse(true);
            sel.removeAllRanges();
            sel.addRange(range);
        }
        updateBlockCursor();
    }

    // ── Block Cursor ──────────────────────────────────────────

    function startCursorBlink() {
        if (_cursorBlinkInterval) clearInterval(_cursorBlinkInterval);
        _cursorBlinkInterval = setInterval(function () {
            _cursorVisible = !_cursorVisible;
            if (blockCursorEl) {
                blockCursorEl.style.opacity = _cursorVisible ? '1' : '0';
            }
        }, 530);
    }

    function updateBlockCursor() {
        if (!blockCursorEl || !codeEl) return;
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) {
            blockCursorEl.style.display = 'none';
            return;
        }
        var range = sel.getRangeAt(0);
        var rects = range.getClientRects();
        var rect = null;
        if (rects.length > 0) {
            rect = rects[0];
        } else {
            rect = range.getBoundingClientRect();
        }

        // Empty line fallback: if rect is 0x0, calculate from line number + font metrics
        if (!rect || (rect.width === 0 && rect.height === 0)) {
            var pos = getCursorPosition();
            var text = getText();
            var lines = text.split('\n');
            var line = 0, charsBefore = 0;
            for (var i = 0; i < lines.length; i++) {
                if (charsBefore + lines[i].length >= pos) { line = i; break; }
                charsBefore += lines[i].length + 1;
            }
            var lineHeight = 13 * 1.5; // font-size * line-height
            var editorRect = editorEl.getBoundingClientRect();
            var scrollTop = editorEl.scrollTop || 0;
            var left = 50 + 12; // line-numbers width + code padding
            var top = 8 + line * lineHeight - scrollTop; // padding + line*height - scroll
            blockCursorEl.style.left = left + 'px';
            blockCursorEl.style.top = top + 'px';
            blockCursorEl.style.height = lineHeight + 'px';
            blockCursorEl.style.display = 'block';
            _cursorVisible = true;
            blockCursorEl.style.opacity = '1';
            return;
        }

        var editorRect = editorEl.getBoundingClientRect();
        var scrollLeft = editorEl.scrollLeft || 0;
        var scrollTop = editorEl.scrollTop || 0;

        var left = rect.left - editorRect.left + scrollLeft;
        var top = rect.top - editorRect.top + scrollTop;

        blockCursorEl.style.left = left + 'px';
        blockCursorEl.style.top = top + 'px';
        blockCursorEl.style.height = rect.height + 'px';
        blockCursorEl.style.display = 'block';

        _cursorVisible = true;
        blockCursorEl.style.opacity = '1';
    }

    // ── Content ──────────────────────────────────────────────

    function getText() {
        if (!codeEl) return '';
        // Use textContent (raw DOM) — consistent with Range.toString() used by getCursorPosition().
        // innerText can return different lengths in EdgeHTML due to whitespace normalization.
        var text = codeEl.textContent || '';
        text = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
        return text;
    }

    function setText(text, cursorPos) {
        if (!codeEl) return;
        codeEl.textContent = text;
        _dirty = false;
        _undoStack = [];
        _redoStack = [];
        pushUndoState();
        updateLineNumbers();
        scheduleHighlight();
        if (typeof cursorPos === 'number' && cursorPos >= 0) {
            setCursorPosition(cursorPos);
        }
    }

    function insertText(text) {
        _dirty = true;
        document.execCommand('insertText', false, text);
    }

    function deleteSelection() {
        var sel = window.getSelection();
        if (sel && !sel.isCollapsed) {
            document.execCommand('delete', false, null);
        }
    }

    function backspace() {
        _dbg('backspace-before');
        var sel = window.getSelection();
        if (sel && !sel.isCollapsed) {
            deleteSelection();
            _dirty = true;
            _dbg('backspace-selDel');
            return;
        }
        // Use execCommand for undo support
        _dirty = true;
        document.execCommand('delete', false, null);
        _dbg('backspace-after');
    }

    function deleteChar() {
        var sel = window.getSelection();
        if (sel && !sel.isCollapsed) {
            deleteSelection();
            return;
        }
        document.execCommand('forwardDelete', false, null);
    }

    function insertNewline() {
        _dbg('newline-before');
        _dirty = true;
        // Get current line indent for auto-indent
        var indent = getCurrentLineIndent();
        document.execCommand('insertText', false, '\n' + indent);
        _dbg('newline-after');
    }

    function getCurrentLineIndent() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return '';
        var range = sel.getRangeAt(0);
        var node = range.startContainer;
        var offset = range.startOffset;

        // Walk backward from cursor to find start of line
        var text = getText();
        var pos = getCursorPosition();
        if (pos < 0) return '';

        var lineStart = text.lastIndexOf('\n', pos - 1) + 1;
        var lineText = text.substring(lineStart, pos);
        var match = lineText.match(/^(\s*)/);
        return match ? match[1] : '';
    }

    // ── Cursor ───────────────────────────────────────────────

    function moveCursorLeft(n) {
        var pos = getCursorPosition();
        var text = getText();
        var newPos = Math.max(0, pos - n);
        setCursorPosition(newPos);
        _dbg('left', 'n=' + n);
    }

    function moveCursorRight(n) {
        var pos = getCursorPosition();
        var text = getText();
        var newPos = Math.min(text.length, pos + n);
        setCursorPosition(newPos);
        _dbg('right', 'n=' + n);
    }

    function moveCursorUp(n) {
        _dbg('up-before', 'n=' + n);
        moveCursorByLine(-n);
        _dbg('up-after', 'n=' + n);
    }

    function moveCursorDown(n) {
        _dbg('down-before', 'n=' + n);
        moveCursorByLine(n);
        _dbg('down-after', 'n=' + n);
    }

    function moveCursorByLine(direction) {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        var pos = getCursorPosition();
        var text = getText();
        var lines = text.split('\n');

        // Find current line
        var currentLine = 0;
        var charsBeforeLine = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBeforeLine + lines[i].length >= pos) {
                currentLine = i;
                break;
            }
            charsBeforeLine += lines[i].length + 1; // +1 for \n
        }

        var col = pos - charsBeforeLine;
        var targetLine = Math.max(0, Math.min(lines.length - 1, currentLine + direction));
        var targetCol = Math.min(col, lines[targetLine].length);

        var newPos = 0;
        for (var i = 0; i < targetLine; i++) newPos += lines[i].length + 1;
        newPos += targetCol;

        _logBuffer.push('[byLine] dir=' + direction +
            ' cur=' + currentLine + ',' + col + '(pos=' + pos + ')' +
            ' tgt=' + targetLine + ',' + targetCol + '(newPos=' + newPos + ')' +
            ' lines=' + lines.length + ' totalLen=' + text.length);

        setCursorPosition(newPos);
    }

    function moveToLineStart() {
        var text = getText();
        var pos = getCursorPosition();
        var lineStart = text.lastIndexOf('\n', pos - 1) + 1;
        setCursorPosition(lineStart);
        _dbg('lineStart');
    }

    function moveToLineEnd() {
        var text = getText();
        var pos = getCursorPosition();
        var lineEnd = text.indexOf('\n', pos);
        if (lineEnd === -1) lineEnd = text.length;
        setCursorPosition(lineEnd);
        _dbg('lineEnd');
    }

    function jumpWordLeft() {
        var text = getText();
        var pos = getCursorPosition();
        if (pos <= 0) return;

        var i = pos - 1;
        // Skip whitespace
        while (i > 0 && /\s/.test(text[i])) i--;
        // Skip word characters
        while (i > 0 && /\S/.test(text[i - 1])) i--;

        setCursorPosition(i);
    }

    function jumpWordRight() {
        var text = getText();
        var pos = getCursorPosition();
        if (pos >= text.length) return;

        var i = pos;
        // Skip current word
        while (i < text.length && /\S/.test(text[i])) i++;
        // Skip whitespace
        while (i < text.length && /\s/.test(text[i])) i++;

        setCursorPosition(i);
    }

    function jumpParagraphUp() {
        var text = getText();
        var pos = getCursorPosition();
        var lines = text.split('\n');

        var currentLine = 0;
        var charsBefore = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBeforeLine + lines[i].length >= pos) { currentLine = i; break; }
            charsBefore += lines[i].length + 1;
        }

        // Find previous blank line
        for (var i = currentLine - 1; i >= 0; i--) {
            if (lines[i].trim() === '') {
                var newPos = 0;
                for (var j = 0; j <= i; j++) newPos += lines[j].length + 1;
                setCursorPosition(newPos);
                return;
            }
        }
        setCursorPosition(0);
    }

    function jumpParagraphDown() {
        var text = getText();
        var pos = getCursorPosition();
        var lines = text.split('\n');

        var currentLine = 0;
        var charsBefore = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBefore + lines[i].length >= pos) { currentLine = i; break; }
            charsBefore += lines[i].length + 1;
        }

        // Find next blank line
        for (var i = currentLine + 1; i < lines.length; i++) {
            if (lines[i].trim() === '') {
                var newPos = 0;
                for (var j = 0; j <= i; j++) newPos += lines[j].length + 1;
                setCursorPosition(newPos);
                return;
            }
        }
        // End of file
        setCursorPosition(text.length);
    }

    function jumpPageUp() {
        var lines = getText().split('\n');
        var pos = getCursorPosition();
        var currentLine = 0;
        var charsBefore = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBefore + lines[i].length >= pos) { currentLine = i; break; }
            charsBefore += lines[i].length + 1;
        }

        var pageLines = Math.max(1, Math.floor(getViewportLines() * 0.8));
        var targetLine = Math.max(0, currentLine - pageLines);

        var newPos = 0;
        for (var i = 0; i < targetLine; i++) newPos += lines[i].length + 1;
        setCursorPosition(newPos);
    }

    function jumpPageDown() {
        var lines = getText().split('\n');
        var pos = getCursorPosition();
        var currentLine = 0;
        var charsBefore = 0;
        for (var i = 0; i < lines.length; i++) {
            if (charsBefore + lines[i].length >= pos) { currentLine = i; break; }
            charsBefore += lines[i].length + 1;
        }

        var pageLines = Math.max(1, Math.floor(getViewportLines() * 0.8));
        var targetLine = Math.min(lines.length - 1, currentLine + pageLines);

        var newPos = 0;
        for (var i = 0; i < targetLine; i++) newPos += lines[i].length + 1;
        setCursorPosition(newPos);
    }

    function getViewportLines() {
        if (!codeEl) return 24;
        var lineHeight = 20; // approximate
        return Math.floor(codeEl.clientHeight / lineHeight) || 24;
    }

    // ── Cursor position (TextWalker-based) ──────────────────

    function countCharsUpTo(container, offset) {
        // Convert a DOM range position (container, offset) to a flat text offset
        // using TreeWalker. Consistent with setCursorPosition's counting.
        if (container === codeEl) return offset;
        var walker = document.createTreeWalker(codeEl, NodeFilter.SHOW_TEXT, null, false);
        var currentOffset = 0;
        while (walker.nextNode()) {
            if (walker.currentNode === container) {
                return currentOffset + offset;
            }
            currentOffset += walker.currentNode.textContent.length;
        }
        return currentOffset;
    }

    function getCursorPosition() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return 0;
        var range = sel.getRangeAt(0);
        var result = countCharsUpTo(range.startContainer, range.startOffset);
        var maxLen = getText().length;
        return result > maxLen ? maxLen : result;
    }

    function setCursorPosition(offset) {
        if (!codeEl) return;
        var text = getText();
        var clamped = Math.max(0, Math.min(offset, text.length));

        var sel = window.getSelection();
        var range = document.createRange();

        var found = walkTextNodes(codeEl, clamped, range);
        if (found) {
            sel.removeAllRanges();
            sel.addRange(range);
        }

        // Verify: read back what we just set
        var actual = getCursorPosition();
        if (actual !== clamped) {
            _logBuffer.push('[setPos-MISMATCH] requested=' + clamped + ' actual=' + actual + ' delta=' + (actual - clamped));
        }

        scrollCursorIntoView();
    }

    function walkTextNodes(root, targetOffset, range) {
        // Use TreeWalker to visit ALL text nodes in document order,
        // including text inside <span> elements (from highlight.js).
        // The old recursive approach missed text inside ELEMENT nodes
        // (only handled TEXT_NODE, DIV, BR), causing setCursorPosition
        // to land at wrong positions after syntax highlighting.
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false);
        var currentOffset = 0;
        while (walker.nextNode()) {
            var node = walker.currentNode;
            var len = node.textContent.length;
            if (currentOffset + len >= targetOffset) {
                range.setStart(node, targetOffset - currentOffset);
                range.collapse(true);
                return true;
            }
            currentOffset += len;
        }
        // If we walked past everything, place at end of last text node
        if (walker.currentNode) {
            range.setStart(walker.currentNode, walker.currentNode.textContent.length);
            range.collapse(true);
            return true;
        }
        return false;
    }

    function scrollCursorIntoView() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;
        var range = sel.getRangeAt(0);
        var rect = range.getBoundingClientRect();
        var editorRect = editorEl.getBoundingClientRect();

        if (rect.bottom > editorRect.bottom) {
            editorEl.scrollTop += rect.bottom - editorRect.bottom + 20;
        }
        if (rect.top < editorRect.top) {
            editorEl.scrollTop -= editorRect.top - rect.top + 20;
        }
        // Update block cursor after scroll
        updateBlockCursor();
    }

    // ── Selection ────────────────────────────────────────────

    var _anchorPos = -1;

    function toggleSelectionAnchor() {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return;

        if (_anchorPos < 0) {
            // Set anchor at current cursor position
            _anchorPos = getCursorPosition();
        } else {
            // Extend selection from anchor to current position
            var currentPos = getCursorPosition();
            var start = Math.min(_anchorPos, currentPos);
            var end = Math.max(_anchorPos, currentPos);

            var range = document.createRange();
            setRangeToOffset(range, start);
            range.setEnd(findNodeAtOffset(end).node, findNodeAtOffset(end).offset);
            sel.removeAllRanges();
            sel.addRange(range);

            _anchorPos = -1; // Reset anchor
        }
    }

    function findNodeAtOffset(offset) {
        var walker = document.createTreeWalker(codeEl, NodeFilter.SHOW_TEXT, null, false);
        var currentOffset = 0;
        var result = { node: codeEl, offset: 0 };

        while (walker.nextNode()) {
            var node = walker.currentNode;
            var len = node.textContent.length;
            if (currentOffset + len >= offset) {
                result.node = node;
                result.offset = offset - currentOffset;
                return result;
            }
            currentOffset += len;
        }
        // Place at end
        if (walker.currentNode) {
            result.node = walker.currentNode;
            result.offset = walker.currentNode.textContent.length;
        }
        return result;
    }

    function setRangeToOffset(range, offset) {
        var result = findNodeAtOffset(offset);
        range.setStart(result.node, result.offset);
        range.collapse(true);
    }

    function hasSelection() {
        var sel = window.getSelection();
        return sel && !sel.isCollapsed;
    }

    function getSelectionRange() {
        var sel = window.getSelection();
        if (!sel || sel.isCollapsed) return null;
        var range = sel.getRangeAt(0);
        var start = countCharsUpTo(range.startContainer, range.startOffset);
        var end = countCharsUpTo(range.endContainer, range.endOffset);
        return { start: start, end: end };
    }

    // ── Syntax ───────────────────────────────────────────────

    function setLanguage(lang) {
        _language = lang || '';
        if (codeEl) {
            codeEl.className = _language ? ('language-' + _language) : '';
        }
        scheduleHighlight();
    }

    function scheduleHighlight() {
        if (!_highlightEnabled) return;
        if (_highlightTimeout) clearTimeout(_highlightTimeout);
        _highlightTimeout = setTimeout(refreshHighlight, 150);
    }

    function refreshHighlight() {
        if (!_highlightEnabled || !codeEl || typeof hljs === 'undefined') return;
        try {
            hljs.highlightBlock(codeEl);
        } catch (e) {
            // Highlight failed — non-critical
        }
    }

    function setHighlightEnabled(enabled) {
        _highlightEnabled = enabled;
        if (enabled) {
            scheduleHighlight();
        } else if (codeEl) {
            // Remove highlight spans, keep plain text
            codeEl.textContent = codeEl.textContent;
            codeEl.className = '';
        }
    }

    // ── Undo / Redo ──────────────────────────────────────────

    function pushUndoState() {
        var text = getText();
        if (text === _lastContent) return;
        _lastContent = text;
        _undoStack.push(text);
        if (_maxUndo > 0 && _undoStack.length > _maxUndo) {
            _undoStack.shift();
        }
        _redoStack = [];
    }

    function undo() {
        if (_undoStack.length <= 1) return;
        _redoStack.push(_undoStack.pop());
        var state = _undoStack[_undoStack.length - 1];
        setTextDirect(state);
    }

    function redo() {
        if (_redoStack.length === 0) return;
        var state = _redoStack.pop();
        _undoStack.push(state);
        setTextDirect(state);
    }

    function setTextDirect(text) {
        if (!codeEl) return;
        codeEl.textContent = text;
        _lastContent = text;
        _dirty = _undoStack.length > 1;
        updateLineNumbers();
        scheduleHighlight();
    }

    function canUndo() { return _undoStack.length > 1; }
    function canRedo() { return _redoStack.length > 0; }

    // ── Word Wrap ────────────────────────────────────────────

    function toggleWordWrap() {
        _wordWrap = !_wordWrap;
        if (codeEl) {
            codeEl.style.whiteSpace = _wordWrap ? 'pre-wrap' : 'pre';
        }
        return _wordWrap;
    }

    function isWordWrapEnabled() { return _wordWrap; }

    // ── Line Numbers ─────────────────────────────────────────

    function updateLineNumbers() {
        if (!lineNumbersEl || !codeEl) return;
        var text = getText();
        var lineCount = text.split('\n').length;
        var html = '';
        for (var i = 1; i <= lineCount; i++) {
            html += '<div>' + i + '</div>';
        }
        lineNumbersEl.innerHTML = html;
    }

    function getLineCount() {
        return getText().split('\n').length;
    }

    // ── State ────────────────────────────────────────────────

    function isDirty() { return _dirty; }
    function setDirty(v) { _dirty = v; }
    function getCharCount() { return getText().length; }

    function setMaxUndo(max) { _maxUndo = max; }

    // ── Normalize EdgeHTML quirks ────────────────────────────

    function normalizeContent() {
        if (!codeEl) return;
        // EdgeHTML inserts <div> for Enter — convert to text nodes with \n
        var divs = codeEl.querySelectorAll('div');
        for (var i = 0; i < divs.length; i++) {
            var div = divs[i];
            var parent = div.parentNode;
            if (!parent) continue;

            // Insert \n text node before the <div>
            var newline = document.createTextNode('\n');
            parent.insertBefore(newline, div);

            // Move children out of the <div>
            while (div.firstChild) {
                parent.insertBefore(div.firstChild, div);
            }
            parent.removeChild(div);
        }
    }

    // ── Scroll (for right stick) ─────────────────────────────

    function scrollViewport(dx, dy) {
        if (editorEl) {
            editorEl.scrollLeft += dx;
            editorEl.scrollTop += dy;
        }
    }

    // ── Font size ────────────────────────────────────────────

    var _fontSize = 13;
    var _fontSizeMin = 8;
    var _fontSizeMax = 32;

    function changeFontSize(delta) {
        _fontSize = Math.max(_fontSizeMin, Math.min(_fontSizeMax, _fontSize + delta));
        applyFontSize();
        return _fontSize;
    }

    function getFontSize() { return _fontSize; }

    function applyFontSize() {
        var s = _fontSize + 'px';
        if (codeEl) codeEl.style.fontSize = s;
        if (lineNumbersEl) lineNumbersEl.style.fontSize = s;
        blockCursorEl.style.width = (_charWidth * _fontSize / 13) + 'px';
        updateBlockCursor();
    }

    // ── Pointer → text position ──────────────────────────────

    var _lineNumbersWidth = 50;

    function getTextPositionAtPoint(viewportX, viewportY) {
        // Clamp X past line-numbers gutter (50px)
        var x = Math.max(viewportX, _lineNumbersWidth);
        var y = viewportY;

        // caretRangeFromPoint expects viewport coordinates (same as what C# passes)
        var range = null;
        if (document.caretRangeFromPoint) {
            range = document.caretRangeFromPoint(x, y);
        } else if (document.caretPositionFromPoint) {
            var pos = document.caretPositionFromPoint(x, y);
            if (pos) {
                range = document.createRange();
                range.setStart(pos.offsetNode, pos.offset);
                range.collapse(true);
            }
        }
        if (!range) {
            _logBuffer.push('[ptr-range] null at (' + x + ',' + y + ')');
            return -1;
        }

        // Only accept positions inside #code (not line-numbers or other elements)
        if (!codeEl.contains(range.startContainer)) {
            _logBuffer.push('[ptr-range] outside codeEl at (' + x + ',' + y + ') container=' + range.startContainer.nodeName);
            return -1;
        }

        // Calculate offset from range — use countCharsUpTo for consistency with setCursorPosition
        var result = countCharsUpTo(range.startContainer, range.startOffset);
        _logBuffer.push('[ptr-range] ok (' + x + ',' + y + ') offset=' + result);
        return result;
    }

    function setTextCursorAtPoint(viewportX, viewportY) {
        _dbg('ptr-before');
        var pos = getTextPositionAtPoint(viewportX, viewportY);
        if (pos >= 0) {
            setCursorPosition(pos);
        }
        _dbg('ptr-after', 'ptr=(' + viewportX + ',' + viewportY + ') pos=' + pos);
    }

    // ── Mouse cursor (LStick on Xbox) ────────────────────────

    function moveMouse(deltaX, deltaY) {
        if (!mouseCursorEl) return;
        // Show on first move
        mouseCursorEl.style.display = 'block';

        _mouseX = Math.max(0, Math.min(window.innerWidth, _mouseX + deltaX));
        _mouseY = Math.max(0, Math.min(window.innerHeight, _mouseY + deltaY));
        mouseCursorEl.style.left = _mouseX + 'px';
        mouseCursorEl.style.top = _mouseY + 'px';

        // Sync caret to mouse position
        setTextCursorAtPoint(_mouseX, _mouseY);
    }

    function showMouse() {
        if (!mouseCursorEl) return;
        mouseCursorEl.style.display = 'block';
        // Center in viewport if first show
        if (_mouseX <= 0 && _mouseY <= 0) {
            _mouseX = window.innerWidth / 2;
            _mouseY = window.innerHeight / 2;
        }
        mouseCursorEl.style.left = _mouseX + 'px';
        mouseCursorEl.style.top = _mouseY + 'px';
    }

    function hideMouse() {
        if (mouseCursorEl) mouseCursorEl.style.display = 'none';
    }

    // ── Init on DOM ready ────────────────────────────────────

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Public API
    return {
        getText: getText,
        setText: setText,
        insertText: insertText,
        deleteSelection: deleteSelection,
        backspace: backspace,
        deleteChar: deleteChar,
        insertNewline: insertNewline,
        moveCursorLeft: moveCursorLeft,
        moveCursorRight: moveCursorRight,
        moveCursorUp: moveCursorUp,
        moveCursorDown: moveCursorDown,
        moveToLineStart: moveToLineStart,
        moveToLineEnd: moveToLineEnd,
        jumpWordLeft: jumpWordLeft,
        jumpWordRight: jumpWordRight,
        jumpParagraphUp: jumpParagraphUp,
        jumpParagraphDown: jumpParagraphDown,
        jumpPageUp: jumpPageUp,
        jumpPageDown: jumpPageDown,
        getCursorPosition: getCursorPosition,
        setCursorPosition: setCursorPosition,
        hasSelection: hasSelection,
        getSelectionRange: getSelectionRange,
        setLanguage: setLanguage,
        refreshHighlight: refreshHighlight,
        setHighlightEnabled: setHighlightEnabled,
        undo: undo,
        redo: redo,
        canUndo: canUndo,
        canRedo: canRedo,
        toggleWordWrap: toggleWordWrap,
        isWordWrapEnabled: isWordWrapEnabled,
        updateLineNumbers: updateLineNumbers,
        getLineCount: getLineCount,
        isDirty: isDirty,
        setDirty: setDirty,
        getCharCount: getCharCount,
        setMaxUndo: setMaxUndo,
        scrollViewport: scrollViewport,
        setTextCursorAtPoint: setTextCursorAtPoint,
        updateBlockCursor: updateBlockCursor,
        moveMouse: moveMouse,
        showMouse: showMouse,
        hideMouse: hideMouse,
        getLogs: _getLogs
    };
})();
