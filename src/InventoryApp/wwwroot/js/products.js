/* =============================================================================
   Products screen.

   The whole list - summary cards, table and pager - is one region of the page
   (#productsPanel) that the server renders. Every interaction updates a small
   piece of state, asks the server for that region again, and swaps it in. The
   page itself never reloads.

   Four things change the state:
       1. typing in the search box
       2. clicking a summary card
       3. clicking a page number
       4. activating or deactivating a product

   Handlers are bound with delegation ($panel.on(...)) because everything inside
   the panel is replaced on every refresh, so directly bound handlers would be
   thrown away with the old markup.
   ============================================================================= */

$(function () {
    'use strict';

    var $panel = $('#productsPanel');
    var $search = $('#productSearch');
    var $message = $('#listMessage');

    var listUrl = $panel.data('list-url');
    var setActiveUrl = $panel.data('set-active-url');

    // Everything the server needs to render the list.
    var state = {
        search: $search.val() || '',
        filter: $panel.data('filter') || 'All',
        page: 1
    };

    var typingTimer = null;


    /* ---- core -------------------------------------------------------------
       Ask the server for the panel in its current state and swap it in. */
    function refresh() {
        $panel.addClass('is-loading');

        $.get(listUrl, state)
            .done(function (html) {
                $panel.html(html);
            })
            .fail(function () {
                showMessage('Could not load the product list. Please try again.', 'danger');
            })
            .always(function () {
                $panel.removeClass('is-loading');
            });
    }

    /* Show a short status message above the list. */
    function showMessage(text, kind) {
        $message
            .removeClass('d-none alert-success alert-danger')
            .addClass('alert-' + kind)
            .text(text);
    }

    function hideMessage() {
        $message.addClass('d-none');
    }


    /* ---- 1. search --------------------------------------------------------
       Debounced, so one request follows a pause in typing rather than one per
       keystroke. The 'search' event fires when the native clear (x) is used. */
    $search.on('input search', function () {
        window.clearTimeout(typingTimer);

        typingTimer = window.setTimeout(function () {
            state.search = $search.val();
            state.page = 1;
            refresh();
        }, 350);
    });


    /* ---- 2. summary cards act as filters ---------------------------------- */
    $panel.on('click', '[data-filter-card]', function () {
        state.filter = $(this).data('filter-card');
        state.page = 1;
        hideMessage();
        refresh();
    });


    /* ---- 3. paging -------------------------------------------------------- */
    $panel.on('click', '[data-page]', function () {
        state.page = $(this).data('page');
        refresh();
    });


    /* ---- 4. activate / deactivate ----------------------------------------
       The counts on the summary cards move with the product, so the whole panel
       is refreshed afterwards rather than just the one row. */
    $panel.on('click', '[data-set-active]', function () {
        var $button = $(this);

        $button.prop('disabled', true);

        $.post(setActiveUrl, {
            id: $button.data('product-id'),
            activate: $button.data('set-active'),
            __RequestVerificationToken: $('input[name="__RequestVerificationToken"]').val()
        })
            .done(function (result) {
                showMessage(result.message, 'success');
                refresh();
            })
            .fail(function () {
                $button.prop('disabled', false);
                showMessage('Could not update the product. Please try again.', 'danger');
            });
    });
});
