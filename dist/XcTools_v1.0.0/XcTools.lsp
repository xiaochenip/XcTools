;;; ============================================================
;;; XcTools loader (ASCII only - safe for AutoCAD 2021-2026,
;;; ANSI and UTF-8 LISP engines parse this file identically).
;;; All localized messages are printed by XcTools.dll (C# side).
;;; ============================================================
(vl-load-com)

(setq *xc-loaded* nil *xc-sent* nil *xc-retry-reactor* nil)

;; ---- resolve plugin directory -------------------------------
(defun xc:plugin-dir ( / tmp dir)
  ;; 1) path of this very file (APPLOAD / drag-in / acad.lsp load)
  (if (and (= (type *load-pathname*) 'STR) (findfile *load-pathname*))
    (setq dir (vl-filename-directory (findfile *load-pathname*)))
  )
  ;; 2) search support paths
  (if (and (not dir) (setq tmp (findfile "XcTools.lsp")))
    (setq dir (vl-filename-directory tmp))
  )
  ;; 3) registry memory
  (if (not dir)
    (setq dir (vl-registry-read "HKEY_CURRENT_USER\\Software\\XcTools" "InstallDir"))
  )
  ;; 4) ask user once (fallback; result saved to registry)
  (if (not dir)
    (progn
      (setq tmp (getfiled "Locate XcTools.lsp" "" "lsp" 8))
      (if tmp (setq dir (vl-filename-directory tmp)))
    )
  )
  (if dir
    (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "InstallDir" dir)
  )
  dir
)

;; ---- pick DLL by AutoCAD version ----------------------------
;;   ACADVER >= 25.0 -> net8 subdir  (2025/2026)
;;   otherwise       -> net48 subdir (2021-2024)
(defun xc:dll-path (dir / v sub try)
  (setq v (atof (getvar "ACADVER"))
        sub (if (>= v 25.0) "net8" "net48"))
  (setq try (strcat dir "\\" sub "\\XcTools.dll"))
  (if (findfile try)
    try
    (progn
      (setq try (strcat dir "\\XcTools.dll"))
      (if (findfile try) try nil)
    )
  )
)

;; ---- split string by single-char delimiter ------------------
(defun xc:split (str delim / start end res)
  (setq res nil start 1)
  (if (= (type str) 'STR)
    (while (<= start (strlen str))
      (setq end (vl-string-search delim str start))
      (if (null end) (setq end (1+ (strlen str))))
      (setq res (cons (substr str start (- end start)) res))
      (setq start (+ end (strlen delim)))
    )
  )
  (reverse res)
)

;; ---- add dir to TRUSTEDPATHS (exact, normalized dedupe) -----
(defun xc:add-trusted (dir / tp parts i found p dnorm)
  (if dir
    (progn
      (setq tp "")
      (vl-catch-all-apply '(lambda () (setq tp (getvar "TRUSTEDPATHS"))))
      (if (/= (type tp) 'STR) (setq tp ""))
      (setq dnorm (vl-string-right-trim "\\" (vl-string-translate "/" "\\" dir)))
      (setq parts (if (/= tp "") (xc:split tp ";") nil))
      (setq found nil i 0)
      (while (and (not found) (< i (length parts)))
        (setq p (vl-string-right-trim "\\" (vl-string-translate "/" "\\" (nth i parts))))
        (if (= (strcase p) (strcase dnorm)) (setq found T))
        (setq i (1+ i))
      )
      (if (not found)
        (vl-catch-all-apply
          '(lambda ()
             (setvar "TRUSTEDPATHS" (if (= tp "") dir (strcat tp ";" dir)))))
      )
    )
  )
)

;; ---- deferred retry when no doc / editor busy ---------------
(defun xc:retry-on-command (reactor args / )
  (if (and (not *xc-loaded*)
           (not *xc-sent*)
           (not (member (strcase (car args)) (list "NETLOAD" "XCTLOAD"))))
    (progn
      (vlr-remove reactor)
      (setq *xc-retry-reactor* nil)
      (xc:safe-netload)
    )
  )
)

;; ---- NETLOAD the DLL ----------------------------------------
(defun xc:netload ( / dir dll)
  (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastLspRun" (rtos (getvar "CDATE") 2 8))
  (cond
    ((or *xc-loaded* *xc-sent*) nil)
    ((null (setq dir (xc:plugin-dir)))
     (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastStep" "nodir")
    )
    ((progn (xc:add-trusted dir) nil))
    ((null (findfile (setq dll (xc:dll-path dir))))
     (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastStep" "nodll")
    )
    ((not (vl-catch-all-error-p
            (vl-catch-all-apply '(lambda () (command "_.NETLOAD" dll)))))
     (setq *xc-loaded* t)
     (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastStep" "loaded")
    )
    ((not (vl-catch-all-error-p
            (vl-catch-all-apply
              '(lambda ()
                 (vla-SendCommand
                   (vla-get-ActiveDocument (vlax-get-acad-object))
                   (strcat "_.NETLOAD \"" dll "\" (setq *xc-loaded* T) "))))))
     (setq *xc-sent* t)
     (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastStep" "sent")
    )
    (t
     (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools" "LastStep" "waitcmd")
     (if (null *xc-retry-reactor*)
       (setq *xc-retry-reactor*
         (vlr-editor-reactor nil '((:vlr-commandEnded . xc:retry-on-command)))))
    )
  )
  (princ)
)

;; ---- safe wrapper: swallow internal errors ------------------
(defun xc:safe-netload ( / err)
  (setq err (vl-catch-all-apply 'xc:netload))
  (if (vl-catch-all-error-p err)
    (vl-registry-write "HKEY_CURRENT_USER\\Software\\XcTools"
                       "LastStep" (strcat "exc:" (vl-catch-all-error-message err)))
  )
  err
)

;; ---- manual reload ------------------------------------------
(defun c:XCTLOAD ()
  (setq *xc-loaded* nil *xc-sent* nil)
  (xc:safe-netload)
)

;; ---- remove lines containing a token from a text file -------
(defun xc:clean-acadlsp-file (path token / fh line keep removed)
  (if (and path (= (type (vl-file-size path)) 'INT))
    (progn
      (setq fh (open path "r") keep nil removed 0)
      (while (setq line (read-line fh))
        (if (vl-string-search token line)
          (setq removed (1+ removed))
          (setq keep (cons line keep))
        )
      )
      (close fh)
      (if (> removed 0)
        (progn
          (setq fh (open path "w"))
          (foreach l (reverse keep) (write-line l fh))
          (close fh)
        )
      )
      removed
    )
    0
  )
)

;; ---- XCTUNSET: remove autoloader from all acad.lsp ----------
(defun c:XCTUNSET ( / prefixes ver rel dir n total)
  (setq total 0
        prefixes (list (getenv "ROAMABLEROOTPREFIX") (getenv "LOCALROOTPREFIX")))
  (foreach p prefixes
    (if p
      (foreach ver (vl-directory-files p "AutoCAD *" 1)
        (foreach rel (vl-directory-files (strcat p ver) "*" 1)
          (setq dir (strcat p ver "\\" rel "\\Support"))
          (if (vl-file-directory-p dir)
            (progn
              (setq n (xc:clean-acadlsp-file (strcat dir "\\acad.lsp") "XcTools.lsp"))
              (setq total (+ total n))
            )
          )
        )
      )
    )
  )
  (princ)
)

;; ---- hook s::startup (append-only, no duplicates) -----------
(defun xc:startup-hook ()
  (xc:safe-netload)
)

(if (null s::startup)
  (defun-q s::startup () (xc:startup-hook))
  (if (and (listp s::startup)
           (not (member 'xc:startup-hook s::startup)))
    (setq s::startup (append s::startup '(xc:startup-hook)))
  )
)

;; ---- if a document is already open (drag-in / APPLOAD) ------
(vl-catch-all-apply
  '(lambda ()
     (if (not (vl-catch-all-error-p
                (vl-catch-all-apply
                  '(lambda () (vla-get-ActiveDocument (vlax-get-acad-object))))))
       (xc:safe-netload)
     )
   )
)

(princ)
